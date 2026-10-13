import asyncio
import unittest

from transport.plugin_hub import PluginHub
from transport.plugin_registry import PluginRegistry


class PluginHubSchedulerTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.registry = PluginRegistry()
        await self.registry.register(
            "session-a",
            "ProjectA",
            "hash-a",
            "2022.3",
        )
        await self.registry.register(
            "session-b",
            "ProjectB",
            "hash-b",
            "2022.3",
        )
        PluginHub.configure(self.registry, asyncio.get_running_loop())
        self.original_send_command = PluginHub.send_command

    async def asyncTearDown(self):
        PluginHub.send_command = self.original_send_command

    async def test_same_unity_instance_commands_use_one_queue(self):
        first_started = asyncio.Event()
        release_first = asyncio.Event()
        timeline: list[str] = []

        async def fake_send_command(cls, session_id, command_type, params):
            timeline.append(f"{command_type}:start")
            if command_type == "first":
                first_started.set()
                await release_first.wait()
            timeline.append(f"{command_type}:end")
            return {"status": "success", "result": command_type}

        PluginHub.send_command = classmethod(fake_send_command)

        first_task = asyncio.create_task(
            PluginHub.send_command_for_instance("hash-a", "first", {})
        )
        await first_started.wait()
        second_task = asyncio.create_task(
            PluginHub.send_command_for_instance("hash-a", "second", {})
        )
        await asyncio.sleep(0.05)

        self.assertEqual(timeline, ["first:start"])

        snapshot = await PluginHub.get_scheduler_snapshot()
        self.assertEqual(snapshot["hash-a"]["queue_depth"], 1)
        self.assertTrue(snapshot["hash-a"]["running"])

        release_first.set()
        results = await asyncio.gather(first_task, second_task)

        self.assertEqual(results[0]["result"], "first")
        self.assertEqual(results[1]["result"], "second")
        self.assertEqual(
            timeline,
            ["first:start", "first:end", "second:start", "second:end"],
        )

    async def test_different_unity_instances_do_not_block_each_other(self):
        first_started = asyncio.Event()
        second_started = asyncio.Event()
        release_first = asyncio.Event()
        timeline: list[str] = []

        async def fake_send_command(cls, session_id, command_type, params):
            timeline.append(f"{command_type}:start:{session_id}")
            if command_type == "first":
                first_started.set()
                await release_first.wait()
            if command_type == "second":
                second_started.set()
            timeline.append(f"{command_type}:end:{session_id}")
            return {"status": "success", "result": command_type}

        PluginHub.send_command = classmethod(fake_send_command)

        first_task = asyncio.create_task(
            PluginHub.send_command_for_instance("hash-a", "first", {})
        )
        await first_started.wait()

        second_task = asyncio.create_task(
            PluginHub.send_command_for_instance("hash-b", "second", {})
        )
        await asyncio.wait_for(second_started.wait(), timeout=0.2)

        self.assertEqual(
            timeline[:2],
            ["first:start:session-a", "second:start:session-b"],
        )

        release_first.set()
        results = await asyncio.gather(first_task, second_task)

        self.assertEqual(results[0]["result"], "first")
        self.assertEqual(results[1]["result"], "second")

    async def test_reconnected_same_project_hash_uses_same_instance_queue(self):
        await self.registry.register(
            "session-a-reconnected",
            "ProjectA",
            "hash-a",
            "2022.3",
        )
        first_started = asyncio.Event()
        release_first = asyncio.Event()
        timeline: list[str] = []

        async def fake_send_command(cls, session_id, command_type, params):
            timeline.append(f"{command_type}:start:{session_id}")
            if command_type == "first":
                first_started.set()
                await release_first.wait()
            timeline.append(f"{command_type}:end:{session_id}")
            return {"status": "success", "result": command_type}

        PluginHub.send_command = classmethod(fake_send_command)

        first_task = asyncio.create_task(
            PluginHub.send_command_for_instance("hash-a", "first", {})
        )
        await first_started.wait()

        await self.registry.register(
            "session-a-newer",
            "ProjectA",
            "hash-a",
            "2022.3",
        )
        second_task = asyncio.create_task(
            PluginHub.send_command_for_instance("hash-a", "second", {})
        )
        await asyncio.sleep(0.05)

        self.assertEqual(timeline, ["first:start:session-a-reconnected"])

        release_first.set()
        await asyncio.gather(first_task, second_task)

        self.assertEqual(
            timeline,
            [
                "first:start:session-a-reconnected",
                "first:end:session-a-reconnected",
                "second:start:session-a-newer",
                "second:end:session-a-newer",
            ],
        )

    async def test_diagnostics_reports_scheduler_by_project_hash(self):
        diagnostics = await PluginHub.get_diagnostics(server_version="9.0.3")

        self.assertIn("scheduler", diagnostics)
        self.assertIn("summary", diagnostics)
        self.assertEqual(
            diagnostics["scheduler"]["policy"],
            "parallel_across_unity_instances_serial_per_instance",
        )
        self.assertEqual(
            diagnostics["summary"]["parallel_model"],
            "跨 Unity 项目并行；同一 Unity 项目串行排队。",
        )
        self.assertIn("sessions", diagnostics)
        self.assertEqual(diagnostics["server"]["session_count"], 2)

    async def test_diagnostics_summary_does_not_mark_active_scheduler_state_stale(self):
        async def fake_send_command(cls, session_id, command_type, params):
            return {"status": "success", "result": command_type}

        PluginHub.send_command = classmethod(fake_send_command)

        await PluginHub.send_command_for_instance("hash-a", "ping", {})

        diagnostics = await PluginHub.get_diagnostics(server_version="9.0.3")

        self.assertTrue(diagnostics["scheduler"]["instances"]["hash-a"]["active"])
        self.assertFalse(any(
            "历史调度状态" in item
            for item in diagnostics["summary"]["action_items"]
        ))

    async def test_diagnostics_summary_marks_only_inactive_scheduler_state_stale(self):
        scheduler = PluginHub._scheduler
        self.assertIsNotNone(scheduler)

        async def operation():
            return {"status": "success"}

        await scheduler.run(
            "stale-hash",
            "DisconnectedProject@stale-hash",
            "ping",
            operation,
            session_id="old-session",
        )

        diagnostics = await PluginHub.get_diagnostics(server_version="9.0.3")

        self.assertFalse(diagnostics["scheduler"]["instances"]["stale-hash"]["active"])
        self.assertTrue(any(
            "历史调度状态" in item
            for item in diagnostics["summary"]["action_items"]
        ))

    async def test_diagnostics_summary_explains_no_connected_sessions(self):
        PluginHub.configure(PluginRegistry(), asyncio.get_running_loop())

        diagnostics = await PluginHub.get_diagnostics(server_version="9.0.3")

        self.assertEqual(diagnostics["summary"]["zh_status"], "未连接")
        self.assertEqual(diagnostics["summary"]["session_count"], 0)
        self.assertIn("没有 Unity 项目连接", diagnostics["summary"]["action_items"][0])


if __name__ == "__main__":
    unittest.main()
