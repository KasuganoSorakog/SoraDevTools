"""
Middleware for managing Unity instance selection per session.

This middleware intercepts all tool calls and injects the active Unity instance
into the request-scoped state, allowing tools to access it via ctx.get_state("unity_instance").
"""
from threading import RLock
import logging
from typing import Any

from fastmcp.server.middleware import Middleware, MiddlewareContext

from transport.plugin_hub import PluginHub

logger = logging.getLogger("mcp-for-unity-server")

# Store a global reference to the middleware instance so tools can interact
# with it to set or clear the active unity instance.
_unity_instance_middleware = None
_middleware_lock = RLock()


def _clean_string(value: Any) -> str | None:
    if isinstance(value, str):
        value = value.strip()
        return value or None
    return None


def _dump_meta(meta: Any) -> dict[str, Any] | None:
    if meta is None:
        return None
    try:
        dump_fn = getattr(meta, "model_dump", None)
        if callable(dump_fn):
            dumped = dump_fn(exclude_none=False)
            return dumped if isinstance(dumped, dict) else None
        if isinstance(meta, dict):
            return dict(meta)
    except Exception as exc:
        return {"_error": str(exc)}
    return None


def _mapping_value(mapping: dict[str, Any], *keys: str) -> Any:
    for key in keys:
        if key in mapping:
            return mapping[key]
    return None


def _extract_workspace_roots(codex_meta: dict[str, Any]) -> list[str]:
    raw_workspaces = _mapping_value(
        codex_meta,
        "workspaces",
        "workspace_roots",
        "workspaceRoots",
    )
    roots: list[str] = []

    if isinstance(raw_workspaces, (list, tuple)):
        for workspace in raw_workspaces:
            root = None
            if isinstance(workspace, str):
                root = workspace
            elif isinstance(workspace, dict):
                root = _mapping_value(
                    workspace,
                    "root",
                    "path",
                    "workspace_root",
                    "workspaceRoot",
                    "cwd",
                )
            root = _clean_string(root)
            if root and root not in roots:
                roots.append(root)

    fallback_root = _clean_string(
        _mapping_value(codex_meta, "workspace_root", "workspaceRoot", "cwd")
    )
    if fallback_root and fallback_root not in roots:
        roots.append(fallback_root)

    return roots


def _extract_codex_metadata(meta_dump: dict[str, Any] | None) -> dict[str, Any]:
    if not isinstance(meta_dump, dict):
        return {}

    raw = _mapping_value(
        meta_dump,
        "x-codex-turn-metadata",
        "codex_turn_metadata",
        "codexTurnMetadata",
        "codex",
    )
    if isinstance(raw, dict):
        return raw

    return {}


def extract_request_identity(ctx) -> dict[str, Any]:
    """Extract request identity fields used for session keying and diagnostics."""
    request_context = getattr(ctx, "request_context", None)

    direct_client_id = _clean_string(getattr(ctx, "client_id", None))
    request_context_client_id = _clean_string(
        getattr(request_context, "client_id", None)
    )

    direct_session_id = _clean_string(getattr(ctx, "session_id", None))
    request_context_session_id = _clean_string(
        getattr(request_context, "session_id", None)
    )

    meta_dump = _dump_meta(getattr(request_context, "meta", None))
    codex_meta = _extract_codex_metadata(meta_dump)

    codex_thread_id = _clean_string(
        _mapping_value(codex_meta, "thread_id", "threadId")
    )
    workspace_roots = _extract_workspace_roots(codex_meta)
    server_name = _clean_string(
        _mapping_value(
            codex_meta,
            "mcp_server",
            "mcp_server_name",
            "mcpServer",
            "server_name",
            "server",
            "config_name",
            "configName",
        )
        or (meta_dump and _mapping_value(
            meta_dump,
            "mcp_server",
            "mcp_server_name",
            "mcpServer",
            "server_name",
            "server",
            "config_name",
            "configName",
        ))
    )

    return {
        "client_id": direct_client_id or request_context_client_id,
        "direct_client_id": direct_client_id,
        "request_context_client_id": request_context_client_id,
        "session_id": direct_session_id or request_context_session_id,
        "direct_session_id": direct_session_id,
        "request_context_session_id": request_context_session_id,
        "request_context_meta": meta_dump,
        "codex_metadata": codex_meta,
        "codex_thread_id": codex_thread_id,
        "codex_workspace_roots": workspace_roots,
        "server_name": server_name,
    }


def derive_session_key(identity: dict[str, Any]) -> str | None:
    """Derive the stable active-instance storage key from extracted identity."""
    thread_id = _clean_string(identity.get("codex_thread_id"))
    if thread_id:
        parts = [f"codex:{thread_id}"]
        client_id = _clean_string(identity.get("client_id"))
        if client_id:
            parts.append(f"client:{client_id}")
        workspace_roots = identity.get("codex_workspace_roots")
        if isinstance(workspace_roots, list) and workspace_roots:
            workspace_root = _clean_string(workspace_roots[0])
            if workspace_root:
                parts.append(f"workspace:{workspace_root}")
        server_name = _clean_string(identity.get("server_name"))
        if server_name:
            parts.append(f"server:{server_name}")
        return "|".join(parts)

    client_id = _clean_string(identity.get("client_id"))
    if client_id:
        return f"client:{client_id}"

    session_id = _clean_string(identity.get("session_id"))
    if session_id:
        return f"session:{session_id}"

    return None


def derive_session_keys(identity: dict[str, Any]) -> list[str]:
    """
    Derive all stable lookup aliases for the same MCP caller.

    Some MCP clients expose slightly different identity fields for tool calls
    and resource reads. Keeping aliases prevents set_active_instance from
    applying to tools but disappearing for resources in the same Codex thread.
    """
    keys: list[str] = []

    def add(key: str | None) -> None:
        key = _clean_string(key)
        if key and key not in keys:
            keys.append(key)

    add(derive_session_key(identity))

    thread_id = _clean_string(identity.get("codex_thread_id"))
    client_id = _clean_string(identity.get("client_id"))
    server_name = _clean_string(identity.get("server_name"))
    workspace_roots = identity.get("codex_workspace_roots")
    workspace_root = None
    if isinstance(workspace_roots, list) and workspace_roots:
        workspace_root = _clean_string(workspace_roots[0])

    if thread_id:
        add(f"codex:{thread_id}")
        if client_id:
            add(f"codex:{thread_id}|client:{client_id}")
        if workspace_root:
            add(f"codex:{thread_id}|workspace:{workspace_root}")
        if server_name:
            add(f"codex:{thread_id}|server:{server_name}")

    if client_id:
        add(f"client:{client_id}")

    for raw_session_id in (
        identity.get("session_id"),
        identity.get("direct_session_id"),
        identity.get("request_context_session_id"),
    ):
        session_id = _clean_string(raw_session_id)
        if session_id:
            add(f"session:{session_id}")

    return keys


def get_unity_instance_middleware() -> 'UnityInstanceMiddleware':
    """Get the global Unity instance middleware."""
    global _unity_instance_middleware
    if _unity_instance_middleware is None:
        with _middleware_lock:
            if _unity_instance_middleware is None:
                # Auto-initialize if not set (lazy singleton) to handle import order or test cases
                _unity_instance_middleware = UnityInstanceMiddleware()

    return _unity_instance_middleware


def set_unity_instance_middleware(middleware: 'UnityInstanceMiddleware') -> None:
    """Set the global Unity instance middleware (called during server initialization)."""
    global _unity_instance_middleware
    _unity_instance_middleware = middleware


class UnityInstanceMiddleware(Middleware):
    """
    Middleware that manages per-session Unity instance selection.

    Stores active instance per session_id and injects it into request state
    for all tool and resource calls.
    """

    def __init__(self):
        super().__init__()
        self._active_by_key: dict[str, str] = {}
        self._lock = RLock()

    def get_session_key(self, ctx) -> str | None:
        """
        Derive a stable key for the calling session.

        Prioritizes client_id for stability. If client_id is missing, Codex
        request metadata is used before falling back to FastMCP session id.
        Never falls back to a shared "global" key; that is unsafe when multiple
        Unity projects or MCP clients are connected.
        """
        return derive_session_key(extract_request_identity(ctx))

    def get_session_keys(self, ctx) -> list[str]:
        """Return all stable key aliases for the calling session."""
        return derive_session_keys(extract_request_identity(ctx))

    def set_active_instance(self, ctx, instance_id: str) -> None:
        """Store the active instance for this session."""
        keys = self.get_session_keys(ctx)
        if not keys:
            raise ValueError(
                "Unable to derive a stable MCP client session key. "
                "Provide client_id, Codex thread metadata, or FastMCP session_id."
            )
        with self._lock:
            for key in keys:
                self._active_by_key[key] = instance_id

    def get_active_instance(self, ctx) -> str | None:
        """Retrieve the active instance for this session."""
        keys = self.get_session_keys(ctx)
        if not keys:
            return None
        with self._lock:
            for key in keys:
                active = self._active_by_key.get(key)
                if active:
                    return active
        return None

    def clear_active_instance(self, ctx) -> None:
        """Clear the stored instance for this session."""
        keys = self.get_session_keys(ctx)
        if not keys:
            return
        with self._lock:
            for key in keys:
                self._active_by_key.pop(key, None)

    async def _maybe_autoselect_instance(self, ctx) -> str | None:
        """
        Auto-select the sole Unity instance when no active instance is set.

        Note: This method both *discovers* and *persists* the selection via
        `set_active_instance` as a side-effect, since callers expect the selection
        to stick for subsequent tool/resource calls in the same session.
        """
        try:
            # Import here to avoid circular dependencies / optional transport modules.
            from transport.unity_transport import _current_transport

            transport = _current_transport()
            if PluginHub.is_configured():
                try:
                    sessions_data = await PluginHub.get_sessions()
                    sessions = sessions_data.sessions or {}
                    ids: list[str] = []
                    for session_info in sessions.values():
                        project = getattr(
                            session_info, "project", None) or "Unknown"
                        hash_value = getattr(session_info, "hash", None)
                        if hash_value:
                            ids.append(f"{project}@{hash_value}")
                    if len(ids) == 1:
                        chosen = ids[0]
                        try:
                            self.set_active_instance(ctx, chosen)
                        except ValueError:
                            logger.debug(
                                "Auto-selected %s for this request only; no stable session key available",
                                chosen,
                            )
                        logger.info(
                            "Auto-selected sole Unity instance via PluginHub: %s",
                            chosen,
                        )
                        return chosen
                except (ConnectionError, ValueError, KeyError, TimeoutError, AttributeError) as exc:
                    logger.debug(
                        "PluginHub auto-select probe failed (%s); falling back to stdio",
                        type(exc).__name__,
                        exc_info=True,
                    )
                except Exception as exc:
                    if isinstance(exc, (SystemExit, KeyboardInterrupt)):
                        raise
                    logger.debug(
                        "PluginHub auto-select probe failed with unexpected error (%s); falling back to stdio",
                        type(exc).__name__,
                        exc_info=True,
                    )

            if transport != "http":
                try:
                    # Import here to avoid circular imports in legacy transport paths.
                    from transport.legacy.unity_connection import get_unity_connection_pool

                    pool = get_unity_connection_pool()
                    instances = pool.discover_all_instances(force_refresh=True)
                    ids = [getattr(inst, "id", None) for inst in instances]
                    ids = [inst_id for inst_id in ids if inst_id]
                    if len(ids) == 1:
                        chosen = ids[0]
                        try:
                            self.set_active_instance(ctx, chosen)
                        except ValueError:
                            logger.debug(
                                "Auto-selected %s for this request only; no stable session key available",
                                chosen,
                            )
                        logger.info(
                            "Auto-selected sole Unity instance via stdio discovery: %s",
                            chosen,
                        )
                        return chosen
                except (ConnectionError, ValueError, KeyError, TimeoutError, AttributeError) as exc:
                    logger.debug(
                        "Stdio auto-select probe failed (%s)",
                        type(exc).__name__,
                        exc_info=True,
                    )
                except Exception as exc:
                    if isinstance(exc, (SystemExit, KeyboardInterrupt)):
                        raise
                    logger.debug(
                        "Stdio auto-select probe failed with unexpected error (%s)",
                        type(exc).__name__,
                        exc_info=True,
                    )
        except Exception as exc:
            if isinstance(exc, (SystemExit, KeyboardInterrupt)):
                raise
            logger.debug(
                "Auto-select path encountered an unexpected error (%s)",
                type(exc).__name__,
                exc_info=True,
            )

        return None

    async def _inject_unity_instance(self, context: MiddlewareContext) -> None:
        """Inject active Unity instance into context if available."""
        ctx = context.fastmcp_context

        active_instance = self.get_active_instance(ctx)
        if active_instance and PluginHub.is_configured():
            try:
                await PluginHub._resolve_session_id(active_instance)
            except Exception as exc:
                if isinstance(exc, (SystemExit, KeyboardInterrupt)):
                    raise
                logger.debug(
                    "Stored active Unity instance %s is not currently reachable (%s); retrying auto-select",
                    active_instance,
                    type(exc).__name__,
                )
                self.clear_active_instance(ctx)
                active_instance = None
        if not active_instance:
            active_instance = await self._maybe_autoselect_instance(ctx)
        if active_instance:
            # If using HTTP transport (PluginHub configured), validate session
            # But for stdio transport (no PluginHub needed or maybe partially configured),
            # we should be careful not to clear instance just because PluginHub can't resolve it.
            # The 'active_instance' (Name@hash) might be valid for stdio even if PluginHub fails.

            session_id: str | None = None
            # Only validate via PluginHub if we are actually using HTTP transport
            # OR if we want to support hybrid mode. For now, let's be permissive.
            if PluginHub.is_configured():
                try:
                    # resolving session_id might fail if the plugin disconnected
                    # We only need session_id for HTTP transport routing.
                    # For stdio, we just need the instance ID.
                    session_id = await PluginHub._resolve_session_id(active_instance)
                except (ConnectionError, ValueError, KeyError, TimeoutError) as exc:
                    # If resolution fails, it means the Unity instance is not reachable via HTTP/WS.
                    # If we are in stdio mode, this might still be fine if the user is just setting state?
                    # But usually if PluginHub is configured, we expect it to work.
                    # Let's LOG the error but NOT clear the instance immediately to avoid flickering,
                    # or at least debug why it's failing.
                    logger.debug(
                        "PluginHub session resolution failed for %s: %s; leaving active_instance unchanged",
                        active_instance,
                        exc,
                        exc_info=True,
                    )
                except Exception as exc:
                    # Re-raise unexpected system exceptions to avoid swallowing critical failures
                    if isinstance(exc, (SystemExit, KeyboardInterrupt)):
                        raise
                    logger.error(
                        "Unexpected error during PluginHub session resolution for %s: %s",
                        active_instance,
                        exc,
                        exc_info=True
                    )

            ctx.set_state("unity_instance", active_instance)
            if session_id is not None:
                ctx.set_state("unity_session_id", session_id)

    async def on_call_tool(self, context: MiddlewareContext, call_next):
        """Inject active Unity instance into tool context if available."""
        await self._inject_unity_instance(context)
        return await call_next(context)

    async def on_read_resource(self, context: MiddlewareContext, call_next):
        """Inject active Unity instance into resource context if available."""
        await self._inject_unity_instance(context)
        return await call_next(context)
