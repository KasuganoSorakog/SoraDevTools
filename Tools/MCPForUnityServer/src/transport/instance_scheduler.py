"""Per-Unity-instance command scheduling.

The server may receive many MCP calls concurrently, but a single Unity Editor
must still execute Unity API work on its main thread. This scheduler gives each
stable Unity instance its own queue: commands for the same instance are
serialized, while commands for different instances can run concurrently.
"""

from __future__ import annotations

import asyncio
import time
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any, Awaitable, Callable, TypeVar

T = TypeVar("T")


class InstanceQueueTimeoutError(TimeoutError):
    """Raised when a command cannot enter an instance queue quickly enough."""

    def __init__(
        self,
        *,
        session_id: str,
        instance: str,
        command: str,
        timeout_seconds: float,
    ) -> None:
        self.session_id = session_id
        self.instance = instance
        self.command = command
        self.timeout_seconds = timeout_seconds
        super().__init__(
            f"Unity instance {instance} is busy; command '{command}' waited "
            f"{timeout_seconds:.1f}s for the instance queue"
        )


@dataclass
class _InstanceScheduleState:
    lock: asyncio.Lock = field(default_factory=asyncio.Lock)
    instance: str | None = None
    session_id: str | None = None
    queue_depth: int = 0
    running: bool = False
    current_command: str | None = None
    current_started_at: str | None = None
    current_started_monotonic: float | None = None
    total_started: int = 0
    total_completed: int = 0
    total_failed: int = 0
    total_cancelled: int = 0
    last_status: str | None = None
    last_queue_wait_ms: int | None = None
    last_completed_at: str | None = None
    last_duration_ms: int | None = None
    last_error: dict[str, Any] | None = None
    retire_when_idle_for_session_id: str | None = None


class InstanceCommandScheduler:
    """Serialize commands per stable Unity instance and expose lightweight status."""

    def __init__(self) -> None:
        self._states: dict[str, _InstanceScheduleState] = {}
        self._state_lock = asyncio.Lock()

    async def run(
        self,
        instance_key: str,
        instance: str | None,
        command: str,
        operation: Callable[[], Awaitable[T]],
        *,
        session_id: str | None = None,
        queue_timeout_s: float | None = None,
    ) -> T:
        state = await self._get_state(instance_key, instance, session_id)
        queued_at = time.monotonic()

        async with self._state_lock:
            state.queue_depth += 1
            state.instance = instance or state.instance or instance_key
            state.session_id = session_id or state.session_id
            if (
                session_id
                and state.retire_when_idle_for_session_id
                and state.retire_when_idle_for_session_id != session_id
            ):
                state.retire_when_idle_for_session_id = None

        acquired = False
        try:
            if queue_timeout_s is None:
                await state.lock.acquire()
            else:
                await asyncio.wait_for(
                    state.lock.acquire(),
                    timeout=max(0.0, queue_timeout_s),
                )
            acquired = True
        except asyncio.TimeoutError as exc:
            error = InstanceQueueTimeoutError(
                session_id=session_id or instance_key,
                instance=instance or instance_key,
                command=command,
                timeout_seconds=float(queue_timeout_s or 0.0),
            )
            async with self._state_lock:
                state.queue_depth = max(0, state.queue_depth - 1)
                state.last_status = "error"
                state.last_error = {
                    "code": "queue_timeout",
                    "command": command,
                    "message": str(error),
                    "at": self._now_iso(),
                }
                self._retire_state_if_idle_locked(instance_key, state)
            raise error from exc
        except asyncio.CancelledError:
            async with self._state_lock:
                state.queue_depth = max(0, state.queue_depth - 1)
                state.total_cancelled += 1
                state.last_status = "cancelled"
                state.last_error = {
                    "code": "cancelled",
                    "command": command,
                    "message": "Command was cancelled while waiting for the instance queue",
                    "at": self._now_iso(),
                }
                self._retire_state_if_idle_locked(instance_key, state)
            raise

        started = time.monotonic()
        try:
            async with self._state_lock:
                state.queue_depth = max(0, state.queue_depth - 1)
                state.running = True
                state.current_command = command
                state.current_started_at = self._now_iso()
                state.current_started_monotonic = started
                state.last_queue_wait_ms = int((started - queued_at) * 1000)
                state.total_started += 1

            result = await operation()
            result_error = self._error_from_result(result, command)

            async with self._state_lock:
                state.total_completed += 1
                state.last_completed_at = self._now_iso()
                state.last_duration_ms = int((time.monotonic() - started) * 1000)
                if result_error:
                    state.total_failed += 1
                    state.last_status = "error"
                    state.last_error = result_error
                else:
                    state.last_status = "success"
                    state.last_error = None
                self._retire_state_if_idle_locked(instance_key, state)
            return result
        except asyncio.CancelledError:
            async with self._state_lock:
                state.total_cancelled += 1
                state.last_status = "cancelled"
                state.last_completed_at = self._now_iso()
                state.last_duration_ms = int((time.monotonic() - started) * 1000)
                state.last_error = {
                    "code": "cancelled",
                    "command": command,
                    "message": "Command was cancelled while running on the instance queue",
                    "at": self._now_iso(),
                }
                self._retire_state_if_idle_locked(instance_key, state)
            raise
        except Exception as exc:
            async with self._state_lock:
                state.total_failed += 1
                state.last_status = "error"
                state.last_completed_at = self._now_iso()
                state.last_duration_ms = int((time.monotonic() - started) * 1000)
                state.last_error = {
                    "code": type(exc).__name__,
                    "command": command,
                    "message": str(exc) or type(exc).__name__,
                    "at": self._now_iso(),
                }
                self._retire_state_if_idle_locked(instance_key, state)
            raise
        finally:
            if acquired:
                async with self._state_lock:
                    state.running = False
                    state.current_command = None
                    state.current_started_at = None
                    state.current_started_monotonic = None
                    self._retire_state_if_idle_locked(instance_key, state)
                state.lock.release()

    async def snapshot(self) -> dict[str, dict[str, Any]]:
        async with self._state_lock:
            now = time.monotonic()
            return {
                instance_key: self._snapshot_state(state, now)
                for instance_key, state in self._states.items()
            }

    async def forget_session(self, session_id: str) -> None:
        async with self._state_lock:
            keys = [
                key
                for key, state in self._states.items()
                if key == session_id or state.session_id == session_id
            ]
            for key in keys:
                state = self._states.get(key)
                if not state:
                    continue
                if not state.running and state.queue_depth == 0:
                    self._states.pop(key, None)
                else:
                    state.retire_when_idle_for_session_id = session_id

    async def forget_instance(self, instance_key: str) -> None:
        async with self._state_lock:
            state = self._states.get(instance_key)
            if state and not state.running and state.queue_depth == 0:
                self._states.pop(instance_key, None)
            elif state:
                state.retire_when_idle_for_session_id = state.session_id or instance_key

    async def _get_state(
        self,
        instance_key: str,
        instance: str | None,
        session_id: str | None,
    ) -> _InstanceScheduleState:
        async with self._state_lock:
            state = self._states.get(instance_key)
            if state is None:
                state = _InstanceScheduleState(
                    instance=instance or instance_key,
                    session_id=session_id,
                )
                self._states[instance_key] = state
            elif instance:
                state.instance = instance
            if session_id:
                if (
                    state.retire_when_idle_for_session_id
                    and state.retire_when_idle_for_session_id != session_id
                ):
                    state.retire_when_idle_for_session_id = None
                state.session_id = session_id
            return state

    def _retire_state_if_idle_locked(
        self,
        instance_key: str,
        state: _InstanceScheduleState,
    ) -> None:
        """Remove a retired state only after its original session is fully idle."""
        retire_session_id = state.retire_when_idle_for_session_id
        if not retire_session_id:
            return
        if state.session_id != retire_session_id:
            return
        if state.running or state.queue_depth > 0:
            return
        if self._states.get(instance_key) is state:
            self._states.pop(instance_key, None)

    @staticmethod
    def _snapshot_state(
        state: _InstanceScheduleState,
        now: float,
    ) -> dict[str, Any]:
        running_for_ms = None
        if state.current_started_monotonic is not None:
            running_for_ms = int((now - state.current_started_monotonic) * 1000)

        return {
            "instance": state.instance,
            "session_id": state.session_id,
            "running": state.running,
            "current_command": state.current_command,
            "current_started_at": state.current_started_at,
            "running_for_ms": running_for_ms,
            "queue_depth": state.queue_depth,
            "total_started": state.total_started,
            "total_completed": state.total_completed,
            "total_failed": state.total_failed,
            "total_cancelled": state.total_cancelled,
            "last_status": state.last_status,
            "last_queue_wait_ms": state.last_queue_wait_ms,
            "last_completed_at": state.last_completed_at,
            "last_duration_ms": state.last_duration_ms,
            "last_error": state.last_error,
        }

    @classmethod
    def _error_from_result(cls, result: Any, command: str) -> dict[str, Any] | None:
        if not isinstance(result, dict):
            return None

        status = result.get("status")
        success = result.get("success")
        is_error = success is False or status in {"error", "failed", "failure"}
        if not is_error:
            return None

        error_text = result.get("error") or result.get("message") or "Command returned an error response"
        return {
            "code": result.get("code") or status or "unity_command_error",
            "category": result.get("category"),
            "command": command,
            "message": str(error_text),
            "at": cls._now_iso(),
        }

    @staticmethod
    def _now_iso() -> str:
        return datetime.now(timezone.utc).isoformat()


__all__ = ["InstanceCommandScheduler", "InstanceQueueTimeoutError"]
