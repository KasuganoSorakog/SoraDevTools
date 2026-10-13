import asyncio
import unittest

from transport.instance_scheduler import InstanceCommandScheduler


class InstanceCommandSchedulerTests(unittest.IsolatedAsyncioTestCase):
    async def test_same_session_commands_are_serialized(self):
        scheduler = InstanceCommandScheduler()
        first_started = asyncio.Event()
        release_first = asyncio.Event()
        timeline: list[str] = []

        async def first_operation():
            timeline.append("first:start")
            first_started.set()
            await release_first.wait()
            timeline.append("first:end")
            return "first"

        async def second_operation():
            timeline.append("second:start")
            return "second"

        first_task = asyncio.create_task(
            scheduler.run("session-a", "ProjectA@aaaa", "slow", first_operation)
        )
        await first_started.wait()

        second_task = asyncio.create_task(
            scheduler.run("session-a", "ProjectA@aaaa", "fast", second_operation)
        )
        await asyncio.sleep(0.05)

        self.assertEqual(timeline, ["first:start"])
        snapshot = await scheduler.snapshot()
        self.assertEqual(snapshot["session-a"]["queue_depth"], 1)
        self.assertTrue(snapshot["session-a"]["running"])

        release_first.set()
        results = await asyncio.gather(first_task, second_task)

        self.assertEqual(results, ["first", "second"])
        self.assertEqual(
            timeline,
            ["first:start", "first:end", "second:start"],
        )

    async def test_different_sessions_can_run_concurrently(self):
        scheduler = InstanceCommandScheduler()
        first_started = asyncio.Event()
        second_started = asyncio.Event()
        release_first = asyncio.Event()
        timeline: list[str] = []

        async def first_operation():
            timeline.append("first:start")
            first_started.set()
            await release_first.wait()
            timeline.append("first:end")
            return "first"

        async def second_operation():
            timeline.append("second:start")
            second_started.set()
            return "second"

        first_task = asyncio.create_task(
            scheduler.run("session-a", "ProjectA@aaaa", "slow", first_operation)
        )
        await first_started.wait()

        second_task = asyncio.create_task(
            scheduler.run("session-b", "ProjectB@bbbb", "fast", second_operation)
        )
        await asyncio.wait_for(second_started.wait(), timeout=0.2)

        self.assertEqual(timeline[:2], ["first:start", "second:start"])
        release_first.set()
        results = await asyncio.gather(first_task, second_task)

        self.assertEqual(results, ["first", "second"])

    async def test_queue_timeout_reports_busy_without_running_operation(self):
        scheduler = InstanceCommandScheduler()
        first_started = asyncio.Event()
        release_first = asyncio.Event()
        second_ran = False

        async def first_operation():
            first_started.set()
            await release_first.wait()
            return "first"

        async def second_operation():
            nonlocal second_ran
            second_ran = True
            return "second"

        first_task = asyncio.create_task(
            scheduler.run("session-a", "ProjectA@aaaa", "slow", first_operation)
        )
        await first_started.wait()

        with self.assertRaises(TimeoutError):
            await scheduler.run(
                "session-a",
                "ProjectA@aaaa",
                "fast",
                second_operation,
                queue_timeout_s=0.01,
            )

        self.assertFalse(second_ran)
        snapshot = await scheduler.snapshot()
        self.assertEqual(snapshot["session-a"]["queue_depth"], 0)
        self.assertEqual(snapshot["session-a"]["last_error"]["code"], "queue_timeout")

        release_first.set()
        await first_task

    async def test_cancelled_queued_command_releases_queue_depth(self):
        scheduler = InstanceCommandScheduler()
        first_started = asyncio.Event()
        release_first = asyncio.Event()

        async def first_operation():
            first_started.set()
            await release_first.wait()
            return "first"

        async def second_operation():
            return "second"

        first_task = asyncio.create_task(
            scheduler.run("session-a", "ProjectA@aaaa", "slow", first_operation)
        )
        await first_started.wait()

        second_task = asyncio.create_task(
            scheduler.run("session-a", "ProjectA@aaaa", "fast", second_operation)
        )
        await asyncio.sleep(0.05)
        second_task.cancel()
        with self.assertRaises(asyncio.CancelledError):
            await second_task

        snapshot = await scheduler.snapshot()
        self.assertEqual(snapshot["session-a"]["queue_depth"], 0)
        self.assertEqual(snapshot["session-a"]["last_error"]["code"], "cancelled")

        release_first.set()
        await first_task

    async def test_error_response_is_visible_in_last_error(self):
        scheduler = InstanceCommandScheduler()

        async def operation():
            return {
                "success": False,
                "code": "unity_console_timeout",
                "category": "timeout",
                "error": "Unity did not respond",
            }

        result = await scheduler.run(
            "session-a",
            "ProjectA@aaaa",
            "read_console",
            operation,
        )

        self.assertFalse(result["success"])
        snapshot = await scheduler.snapshot()
        self.assertEqual(snapshot["session-a"]["last_status"], "error")
        self.assertEqual(snapshot["session-a"]["last_error"]["code"], "unity_console_timeout")

    async def test_forget_session_while_running_retires_state_after_idle(self):
        scheduler = InstanceCommandScheduler()
        first_started = asyncio.Event()
        release_first = asyncio.Event()

        async def operation():
            first_started.set()
            await release_first.wait()
            return "done"

        task = asyncio.create_task(
            scheduler.run(
                "hash-a",
                "ProjectA@hash-a",
                "slow",
                operation,
                session_id="session-a",
            )
        )
        await first_started.wait()

        await scheduler.forget_session("session-a")
        snapshot = await scheduler.snapshot()
        self.assertIn("hash-a", snapshot)
        self.assertTrue(snapshot["hash-a"]["running"])

        release_first.set()
        await task

        snapshot = await scheduler.snapshot()
        self.assertNotIn("hash-a", snapshot)

    async def test_forget_old_session_does_not_remove_reconnected_session_state(self):
        scheduler = InstanceCommandScheduler()
        first_started = asyncio.Event()
        release_first = asyncio.Event()
        second_started = asyncio.Event()
        release_second = asyncio.Event()

        async def first_operation():
            first_started.set()
            await release_first.wait()
            return "first"

        async def second_operation():
            second_started.set()
            await release_second.wait()
            return "second"

        first_task = asyncio.create_task(
            scheduler.run(
                "hash-a",
                "ProjectA@hash-a",
                "slow",
                first_operation,
                session_id="old-session",
            )
        )
        await first_started.wait()
        await scheduler.forget_session("old-session")

        second_task = asyncio.create_task(
            scheduler.run(
                "hash-a",
                "ProjectA@hash-a",
                "fast",
                second_operation,
                session_id="new-session",
            )
        )
        await asyncio.sleep(0.05)

        release_first.set()
        self.assertEqual(await first_task, "first")
        await second_started.wait()

        snapshot = await scheduler.snapshot()
        self.assertIn("hash-a", snapshot)
        self.assertEqual(snapshot["hash-a"]["session_id"], "new-session")
        self.assertTrue(snapshot["hash-a"]["running"])

        release_second.set()
        self.assertEqual(await second_task, "second")

        snapshot = await scheduler.snapshot()
        self.assertIn("hash-a", snapshot)
        self.assertEqual(snapshot["hash-a"]["session_id"], "new-session")
        self.assertFalse(snapshot["hash-a"]["running"])


if __name__ == "__main__":
    unittest.main()
