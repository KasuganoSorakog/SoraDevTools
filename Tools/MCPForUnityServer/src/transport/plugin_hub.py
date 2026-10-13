"""WebSocket hub for Unity plugin communication."""

from __future__ import annotations

import asyncio
import logging
import os
import uuid
from typing import Any

from starlette.endpoints import WebSocketEndpoint
from starlette.websockets import WebSocket

from models.models import MCPResponse
from transport.plugin_registry import PluginRegistry
from transport.instance_scheduler import (
    InstanceCommandScheduler,
    InstanceQueueTimeoutError,
)
from transport.models import (
    WelcomeMessage,
    RegisteredMessage,
    ExecuteCommandMessage,
    RegisterMessage,
    RegisterToolsMessage,
    PongMessage,
    CommandResultMessage,
    SessionList,
    SessionDetails,
)

logger = logging.getLogger("mcp-for-unity-server")


def classified_error_response(
    *,
    code: str,
    category: str,
    error: str,
    severity: str = "warning",
    retryable: bool = False,
    retry_after_ms: int | None = None,
    hint: str | None = None,
    data: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Build a backward-compatible MCP error response with machine-readable diagnostics."""

    payload = dict(data or {})
    payload.setdefault("reason", code)
    if retry_after_ms is not None:
        payload.setdefault("retry_after_ms", retry_after_ms)

    return MCPResponse(
        success=False,
        error=error,
        hint=hint,
        code=code,
        category=category,
        severity=severity,
        retryable=retryable,
        retry_after_ms=retry_after_ms,
        data=payload or None,
    ).model_dump()


class PluginDisconnectedError(RuntimeError):
    """Raised when a plugin WebSocket disconnects while commands are in flight."""


class NoUnitySessionError(RuntimeError):
    """Raised when no Unity plugins are available."""


class PluginHub(WebSocketEndpoint):
    """Manages persistent WebSocket connections to Unity plugins."""

    encoding = "json"
    KEEP_ALIVE_INTERVAL = 15
    SERVER_TIMEOUT = 30
    COMMAND_TIMEOUT = 30
    # Timeout (seconds) for fast-fail commands like ping/get_editor_state.
    # Keep short so MCP clients aren't blocked during Unity compilation/reload/unfocused throttling.
    FAST_FAIL_TIMEOUT = 2.0
    # Console reads must run on the Unity main thread and can be slow when the
    # Editor console contains many long entries. Keep a bounded but realistic
    # default instead of the 2s fast-fail budget used by pings/editor-state.
    READ_CONSOLE_TIMEOUT = 15.0
    # Maximum time a command waits to enter a busy Unity instance queue.
    # Cross-project calls use different queues and are not affected by this.
    INSTANCE_QUEUE_TIMEOUT = 25.0
    FAST_QUEUE_TIMEOUT = 2.0
    READ_CONSOLE_QUEUE_TIMEOUT = 5.0
    # Fast-path commands should never block the client for long; return a retry hint instead.
    # This helps avoid the Cursor-side ~30s tool-call timeout when Unity is compiling/reloading
    # or is throttled while unfocused.
    _FAST_FAIL_COMMANDS: set[str] = {
        "get_editor_state", "ping"}

    _registry: PluginRegistry | None = None
    _connections: dict[str, WebSocket] = {}
    # command_id -> {"future": Future, "session_id": str}
    _pending: dict[str, dict[str, Any]] = {}
    _lock: asyncio.Lock | None = None
    _loop: asyncio.AbstractEventLoop | None = None
    _scheduler: InstanceCommandScheduler | None = None

    @classmethod
    def configure(
        cls,
        registry: PluginRegistry,
        loop: asyncio.AbstractEventLoop | None = None,
    ) -> None:
        cls._registry = registry
        cls._loop = loop or asyncio.get_running_loop()
        # Ensure coordination primitives are bound to the configured loop
        cls._lock = asyncio.Lock()
        cls._scheduler = InstanceCommandScheduler()

    @classmethod
    def is_configured(cls) -> bool:
        return (
            cls._registry is not None
            and cls._lock is not None
            and cls._scheduler is not None
        )

    async def on_connect(self, websocket: WebSocket) -> None:
        await websocket.accept()
        msg = WelcomeMessage(
            serverTimeout=self.SERVER_TIMEOUT,
            keepAliveInterval=self.KEEP_ALIVE_INTERVAL,
        )
        await websocket.send_json(msg.model_dump())

    async def on_receive(self, websocket: WebSocket, data: Any) -> None:
        if not isinstance(data, dict):
            logger.warning(f"Received non-object payload from plugin: {data}")
            return

        message_type = data.get("type")
        try:
            if message_type == "register":
                await self._handle_register(websocket, RegisterMessage(**data))
            elif message_type == "register_tools":
                await self._handle_register_tools(websocket, RegisterToolsMessage(**data))
            elif message_type == "pong":
                await self._handle_pong(PongMessage(**data))
            elif message_type == "command_result":
                await self._handle_command_result(CommandResultMessage(**data))
            else:
                logger.debug(f"Ignoring plugin message: {data}")
        except Exception as e:
            logger.error(f"Error handling message type {message_type}: {e}")

    async def on_disconnect(self, websocket: WebSocket, close_code: int) -> None:
        cls = type(self)
        lock = cls._lock
        if lock is None:
            return
        async with lock:
            session_id = next(
                (sid for sid, ws in cls._connections.items() if ws is websocket), None)
            if session_id:
                cls._connections.pop(session_id, None)
                # Fail-fast any in-flight commands for this session to avoid waiting for COMMAND_TIMEOUT.
                pending_ids = [
                    command_id
                    for command_id, entry in cls._pending.items()
                    if entry.get("session_id") == session_id
                ]
                for command_id in pending_ids:
                    entry = cls._pending.get(command_id)
                    future = entry.get("future") if isinstance(
                        entry, dict) else None
                    if future and not future.done():
                        future.set_exception(
                            PluginDisconnectedError(
                                f"Unity plugin session {session_id} disconnected while awaiting command_result"
                            )
                        )
                if cls._registry:
                    await cls._registry.unregister(session_id)
                if cls._scheduler:
                    await cls._scheduler.forget_session(session_id)
                logger.info(
                    f"Plugin session {session_id} disconnected ({close_code})")

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------
    @classmethod
    async def send_command(cls, session_id: str, command_type: str, params: dict[str, Any]) -> dict[str, Any]:
        websocket = await cls._get_connection(session_id)
        command_id = str(uuid.uuid4())
        future: asyncio.Future = asyncio.get_running_loop().create_future()
        # Compute a per-command timeout:
        # - fast-path commands: short timeout (encourage retry)
        # - long-running commands: allow caller to request a longer timeout via params
        unity_timeout_s = float(cls.COMMAND_TIMEOUT)
        server_wait_s = float(cls.COMMAND_TIMEOUT)
        requested = None
        try:
            if isinstance(params, dict):
                requested = params.get("timeout_seconds", None)
                if requested is None:
                    requested = params.get("timeoutSeconds", None)
        except Exception:
            requested = None

        requested_s: float | None = None
        if requested is not None:
            try:
                requested_s = float(requested)
                # Clamp to a sane upper bound to avoid accidental infinite hangs.
                requested_s = max(1.0, min(requested_s, 60.0 * 60.0))
            except Exception:
                requested_s = None

        if command_type in cls._FAST_FAIL_COMMANDS:
            fast_timeout = float(cls.FAST_FAIL_TIMEOUT)
            unity_timeout_s = fast_timeout
            server_wait_s = fast_timeout
        elif command_type == "read_console":
            console_timeout = requested_s or float(cls.READ_CONSOLE_TIMEOUT)
            unity_timeout_s = console_timeout
            server_wait_s = console_timeout + 5.0
        else:
            if requested_s is not None:
                unity_timeout_s = max(unity_timeout_s, requested_s)
                # Give the server a small cushion beyond the Unity-side timeout to account for transport overhead.
                server_wait_s = max(server_wait_s, requested_s + 5.0)

        lock = cls._lock
        if lock is None:
            raise RuntimeError("PluginHub not configured")

        async with lock:
            if command_id in cls._pending:
                raise RuntimeError(
                    f"Duplicate command id generated: {command_id}")
            cls._pending[command_id] = {
                "future": future, "session_id": session_id}

        try:
            msg = ExecuteCommandMessage(
                id=command_id,
                name=command_type,
                params=params,
                timeout=unity_timeout_s,
            )
            try:
                await websocket.send_json(msg.model_dump())
            except Exception as exc:
                # If send fails (socket already closing), fail the future so callers don't hang.
                if not future.done():
                    future.set_exception(exc)
                raise
            try:
                result = await asyncio.wait_for(future, timeout=server_wait_s)
                return result
            except PluginDisconnectedError as exc:
                return classified_error_response(
                    code="unity_session_disconnected",
                    category="session",
                    error=f"{exc}; reconnect from the MCP panel",
                    hint="manual_connect",
                )
            except asyncio.TimeoutError:
                if command_type in cls._FAST_FAIL_COMMANDS:
                    return classified_error_response(
                        code="unity_fast_command_timeout",
                        category="timeout",
                        error=f"Unity did not respond to '{command_type}' within {server_wait_s:.1f}s",
                        data={"command": command_type, "timeout_seconds": server_wait_s},
                    )
                if command_type == "read_console":
                    return classified_error_response(
                        code="unity_console_timeout",
                        category="timeout",
                        error=(
                            f"Unity did not respond to 'read_console' within {server_wait_s:.1f}s; "
                            "the Console may be busy. Use a smaller count/page_size or clear noisy logs."
                        ),
                        data={"command": command_type, "timeout_seconds": server_wait_s},
                    )
                raise
        finally:
            async with lock:
                cls._pending.pop(command_id, None)

    @classmethod
    async def get_sessions(cls) -> SessionList:
        if cls._registry is None:
            return SessionList(sessions={})
        sessions = await cls._registry.list_sessions()
        return SessionList(
            sessions={
                session_id: SessionDetails(
                    project=session.project_name,
                    hash=session.project_hash,
                    unity_version=session.unity_version,
                    connected_at=session.connected_at.isoformat(),
                    project_path=session.project_path,
                    package_version=session.package_version,
                    current_scene=session.current_scene,
                    capabilities_version=session.capabilities_version,
                )
                for session_id, session in sessions.items()
            }
        )

    @classmethod
    async def get_diagnostics(cls, server_version: str | None = None) -> dict[str, Any]:
        """Return central-server diagnostics for editor UI and MCP clients."""

        sessions_data = await cls.get_sessions()
        sessions = sessions_data.sessions
        package_versions = sorted({
            session.package_version or "unknown"
            for session in sessions.values()
        })
        capabilities_versions = sorted({
            session.capabilities_version or "unknown"
            for session in sessions.values()
        })

        warnings: list[dict[str, Any]] = []
        if len(package_versions) > 1:
            warnings.append({
                "code": "package_version_mismatch",
                "severity": "warning",
                "message": "已连接 Unity 项目的 MCP 包版本不一致，建议统一后再进行多项目操作。",
                "versions": package_versions,
            })
        if len(capabilities_versions) > 1:
            warnings.append({
                "code": "capabilities_version_mismatch",
                "severity": "warning",
                "message": "已连接 Unity 项目的能力协议版本不一致，部分工具可能行为不同。",
                "versions": capabilities_versions,
            })

        if server_version:
            server_base_version = cls._base_version(server_version)
            mismatched_sessions = [
                f"{session.project}@{session.hash}"
                for session in sessions.values()
                if session.package_version
                and cls._base_version(session.package_version) != server_base_version
            ]
            if mismatched_sessions:
                warnings.append({
                    "code": "server_package_version_mismatch",
                    "severity": "warning",
                    "message": "中心 Server 版本与部分 Unity 项目的 MCP 包版本不一致，建议统一后重启中心 Server。",
                    "server_version": server_version,
                    "instances": mismatched_sessions,
                })

        session_payload: dict[str, Any] = {}
        scheduler_snapshot = await cls.get_scheduler_snapshot()
        for session_id, session in sessions.items():
            tools = await cls.get_tools_for_project(session.hash)
            session_payload[session_id] = {
                **session.model_dump(),
                "instance": f"{session.project}@{session.hash}",
                "tool_count": len(tools),
                "scheduler": scheduler_snapshot.get(session.hash),
            }

        busy_instances = [
            instance_key
            for instance_key, state in scheduler_snapshot.items()
            if state.get("running") or state.get("queue_depth", 0) > 0
        ]
        active_hashes = {session.hash for session in sessions.values()}
        summary = cls._build_diagnostics_summary(
            session_count=len(sessions),
            warnings=warnings,
            scheduler_snapshot=scheduler_snapshot,
            busy_instance_count=len(busy_instances),
            active_hashes=active_hashes,
        )

        return {
            "status": "ok" if not warnings else "warning",
            "summary": summary,
            "server": {
                "transport": "http",
                "version": server_version or "unknown",
                "session_count": len(sessions),
                "package_versions": package_versions,
                "capabilities_versions": capabilities_versions,
            },
            "scheduler": {
                "policy": "parallel_across_unity_instances_serial_per_instance",
                "busy_instance_count": len(busy_instances),
                "queued_command_count": sum(
                    int(state.get("queue_depth") or 0)
                    for state in scheduler_snapshot.values()
                ),
                "instances": {
                    instance_key: {
                        **state,
                        "active": instance_key in active_hashes,
                    }
                    for instance_key, state in scheduler_snapshot.items()
                },
            },
            "sessions": session_payload,
            "warnings": warnings,
        }

    @classmethod
    def _build_diagnostics_summary(
        cls,
        *,
        session_count: int,
        warnings: list[dict[str, Any]],
        scheduler_snapshot: dict[str, dict[str, Any]],
        busy_instance_count: int,
        active_hashes: set[str] | None = None,
    ) -> dict[str, Any]:
        active_hashes = active_hashes or set()
        action_items: list[str] = []
        if session_count == 0:
            action_items.append("没有 Unity 项目连接到中心 Server。请确认目标项目已打开，并在 MCP For Unity 面板启动/连接 Session。")
        if warnings:
            action_items.append("存在版本或协议不一致，建议统一各项目的 MCP 包和 Server 后重启中心 Server。")
        if busy_instance_count > 0:
            action_items.append("有 Unity 实例正在执行或排队，请稍后重试，或查看 scheduler.instances 中的 current_command。")

        stale_instances = [
            state.get("instance") or instance_key
            for instance_key, state in scheduler_snapshot.items()
            if instance_key not in active_hashes
        ]
        if stale_instances:
            action_items.append("存在历史调度状态但对应 Unity 已断开；通常可忽略，重连后会自动更新。")

        if not action_items:
            action_items.append("中心 Server 正常；不同 Unity 项目可并行处理，同一项目会自动排队保护 Unity 主线程。")

        return {
            "zh_status": "正常" if not warnings and session_count > 0 else ("未连接" if session_count == 0 else "需要注意"),
            "session_count": session_count,
            "busy_instance_count": busy_instance_count,
            "queued_command_count": sum(
                int(state.get("queue_depth") or 0)
                for state in scheduler_snapshot.values()
            ),
            "parallel_model": "跨 Unity 项目并行；同一 Unity 项目串行排队。",
            "action_items": action_items,
        }

    @classmethod
    async def get_scheduler_snapshot(cls) -> dict[str, dict[str, Any]]:
        if cls._scheduler is None:
            return {}
        return await cls._scheduler.snapshot()

    @staticmethod
    def _base_version(version: str) -> str:
        return (version or "unknown").split("+", 1)[0].strip().lower()

    @classmethod
    async def get_tools_for_project(cls, project_hash: str) -> list[Any]:
        """Retrieve tools registered for a active project hash."""
        if cls._registry is None:
            return []

        session_id = await cls._registry.get_session_id_by_hash(project_hash)
        if not session_id:
            return []

        session = await cls._registry.get_session(session_id)
        if not session:
            return []

        return list(session.tools.values())

    @classmethod
    async def get_tool_definition(cls, project_hash: str, tool_name: str) -> Any | None:
        """Retrieve a specific tool definition for an active project hash."""
        if cls._registry is None:
            return None

        session_id = await cls._registry.get_session_id_by_hash(project_hash)
        if not session_id:
            return None

        session = await cls._registry.get_session(session_id)
        if not session:
            return None

        return session.tools.get(tool_name)

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------
    async def _handle_register(self, websocket: WebSocket, payload: RegisterMessage) -> None:
        cls = type(self)
        registry = cls._registry
        lock = cls._lock
        if registry is None or lock is None:
            await websocket.close(code=1011)
            raise RuntimeError("PluginHub not configured")

        project_name = payload.project_name
        project_hash = payload.project_hash
        unity_version = payload.unity_version

        if not project_hash:
            await websocket.close(code=4400)
            raise ValueError(
                "Plugin registration missing project_hash")

        session_id = str(uuid.uuid4())
        # Inform the plugin of its assigned session ID
        response = RegisteredMessage(session_id=session_id)
        await websocket.send_json(response.model_dump())

        session = await registry.register(
            session_id,
            project_name,
            project_hash,
            unity_version,
            project_path=payload.project_path,
            package_version=payload.package_version,
            current_scene=payload.current_scene,
            capabilities_version=payload.capabilities_version,
        )
        async with lock:
            cls._connections[session.session_id] = websocket
        logger.info(f"Plugin registered: {project_name} ({project_hash})")

    async def _handle_register_tools(self, websocket: WebSocket, payload: RegisterToolsMessage) -> None:
        cls = type(self)
        registry = cls._registry
        lock = cls._lock
        if registry is None or lock is None:
            return

        # Find session_id for this websocket
        async with lock:
            session_id = next(
                (sid for sid, ws in cls._connections.items() if ws is websocket), None)

        if not session_id:
            logger.warning("Received register_tools from unknown connection")
            return

        await registry.register_tools_for_session(session_id, payload.tools)
        logger.info(
            f"Registered {len(payload.tools)} tools for session {session_id}")

    async def _handle_command_result(self, payload: CommandResultMessage) -> None:
        cls = type(self)
        lock = cls._lock
        if lock is None:
            return
        command_id = payload.id
        result = payload.result

        if not command_id:
            logger.warning(f"Command result missing id: {payload}")
            return

        async with lock:
            entry = cls._pending.get(command_id)
        future = entry.get("future") if isinstance(entry, dict) else None
        if future and not future.done():
            future.set_result(result)

    async def _handle_pong(self, payload: PongMessage) -> None:
        cls = type(self)
        registry = cls._registry
        if registry is None:
            return
        session_id = payload.session_id
        if session_id:
            await registry.touch(session_id)

    @classmethod
    async def _get_connection(cls, session_id: str) -> WebSocket:
        lock = cls._lock
        if lock is None:
            raise RuntimeError("PluginHub not configured")
        async with lock:
            websocket = cls._connections.get(session_id)
        if websocket is None:
            raise RuntimeError(f"Plugin session {session_id} not connected")
        return websocket

    # ------------------------------------------------------------------
    # Session resolution helpers
    # ------------------------------------------------------------------
    @classmethod
    async def _resolve_session_id(cls, unity_instance: str | None) -> str:
        """Resolve a project hash (Unity instance id) to an active plugin session.

        Resolve once and fail fast when no session is available. Unity reconnects
        are user-driven from the MCP panel to avoid background retry loops.
        """
        if cls._registry is None:
            raise RuntimeError("Plugin registry not configured")

        # Allow callers to provide either just the hash or Name@hash
        target_hash: str | None = None
        if unity_instance:
            if "@" in unity_instance:
                _, _, suffix = unity_instance.rpartition("@")
                target_hash = suffix or None
            else:
                target_hash = unity_instance

        async def _try_once() -> tuple[str | None, int]:
            # Prefer a specific Unity instance if one was requested
            if target_hash:
                session_id = await cls._registry.get_session_id_by_hash(target_hash)
                sessions = await cls._registry.list_sessions()
                return session_id, len(sessions)

            # No target provided: determine if we can auto-select
            sessions = await cls._registry.list_sessions()
            count = len(sessions)
            if count == 0:
                return None, count
            if count == 1:
                return next(iter(sessions.keys())), count
            # Multiple sessions but no explicit target is ambiguous
            return None, count

        session_id, session_count = await _try_once()
        if session_id is None and not target_hash and session_count > 1:
            raise RuntimeError(
                "Multiple Unity instances are connected. "
                "Call set_active_instance with Name@hash from mcpforunity://instances."
            )

        if session_id is None:
            logger.warning(
                "No Unity plugin session available (instance=%s); waiting for manual panel reconnect",
                unity_instance or "default",
            )
            raise NoUnitySessionError(
                "No Unity plugins are currently connected")

        return session_id

    @classmethod
    async def send_command_for_instance(
        cls,
        unity_instance: str | None,
        command_type: str,
        params: dict[str, Any],
    ) -> dict[str, Any]:
        try:
            session_id = await cls._resolve_session_id(unity_instance)
        except NoUnitySessionError:
            logger.debug(
                "Unity session unavailable; waiting for manual reconnect: command=%s instance=%s",
                command_type,
                unity_instance or "default",
            )
            return classified_error_response(
                code="no_unity_session",
                category="session",
                error="Unity session not available; reconnect from the MCP panel",
                hint="manual_connect",
            )

        scheduler = cls._scheduler
        if scheduler is None:
            return await cls._send_command_for_resolved_session(
                session_id,
                command_type,
                params,
            )

        instance_key, instance_label = await cls._describe_instance(session_id, unity_instance)
        try:
            return await scheduler.run(
                instance_key,
                instance_label,
                command_type,
                lambda: cls._send_command_for_resolved_session(
                    session_id,
                    command_type,
                    params,
                ),
                session_id=session_id,
                queue_timeout_s=cls._queue_timeout_for_command(command_type),
            )
        except InstanceQueueTimeoutError as exc:
            return classified_error_response(
                code="unity_instance_busy",
                category="busy",
                error=str(exc),
                data={
                    "command": command_type,
                    "unity_instance": instance_label,
                    "session_id": session_id,
                    "queue_timeout_seconds": exc.timeout_seconds,
                },
            )

    @classmethod
    async def _send_command_for_resolved_session(
        cls,
        session_id: str,
        command_type: str,
        params: dict[str, Any],
    ) -> dict[str, Any]:
        return await cls.send_command(session_id, command_type, params)

    @classmethod
    async def _describe_instance(
        cls,
        session_id: str,
        unity_instance: str | None,
    ) -> tuple[str, str]:
        if cls._registry is None:
            return session_id, unity_instance or session_id
        session = await cls._registry.get_session(session_id)
        if session:
            return session.project_hash, f"{session.project_name}@{session.project_hash}"
        if unity_instance:
            if "@" in unity_instance:
                _, _, suffix = unity_instance.rpartition("@")
                return suffix or unity_instance, unity_instance
            return unity_instance, unity_instance
        return session_id, session_id

    @classmethod
    def _queue_timeout_for_command(cls, command_type: str) -> float | None:
        if command_type in cls._FAST_FAIL_COMMANDS:
            return cls._read_timeout_env(
                "UNITY_MCP_FAST_QUEUE_TIMEOUT_SECONDS",
                cls.FAST_QUEUE_TIMEOUT,
            )
        if command_type == "read_console":
            return cls._read_timeout_env(
                "UNITY_MCP_CONSOLE_QUEUE_TIMEOUT_SECONDS",
                cls.READ_CONSOLE_QUEUE_TIMEOUT,
            )
        return cls._read_timeout_env(
            "UNITY_MCP_INSTANCE_QUEUE_TIMEOUT_SECONDS",
            cls.INSTANCE_QUEUE_TIMEOUT,
        )

    @staticmethod
    def _read_timeout_env(name: str, default: float) -> float | None:
        raw = os.environ.get(name)
        if raw is None:
            value = default
        else:
            try:
                value = float(raw)
            except ValueError:
                logger.warning("Invalid %s=%r, using default %.1f", name, raw, default)
                value = default
        if value <= 0:
            return None
        return max(0.05, min(value, 60.0 * 60.0))

    # ------------------------------------------------------------------
    # Blocking helpers for synchronous tool code
    # ------------------------------------------------------------------
    @classmethod
    def _run_coroutine_sync(cls, coro: "asyncio.Future[Any]") -> Any:
        if cls._loop is None:
            raise RuntimeError("PluginHub event loop not configured")
        loop = cls._loop
        if loop.is_running():
            try:
                running_loop = asyncio.get_running_loop()
            except RuntimeError:
                running_loop = None
            else:
                if running_loop is loop:
                    raise RuntimeError(
                        "Cannot wait synchronously for PluginHub coroutine from within the event loop"
                    )
        future = asyncio.run_coroutine_threadsafe(coro, loop)
        return future.result()

    @classmethod
    def send_command_blocking(
        cls,
        unity_instance: str | None,
        command_type: str,
        params: dict[str, Any],
    ) -> dict[str, Any]:
        return cls._run_coroutine_sync(
            cls.send_command_for_instance(unity_instance, command_type, params)
        )

    @classmethod
    def list_sessions_sync(cls) -> SessionList:
        return cls._run_coroutine_sync(cls.get_sessions())


def send_command_to_plugin(
    *,
    unity_instance: str | None,
    command_type: str,
    params: dict[str, Any],
) -> dict[str, Any]:
    return PluginHub.send_command_blocking(unity_instance, command_type, params)
