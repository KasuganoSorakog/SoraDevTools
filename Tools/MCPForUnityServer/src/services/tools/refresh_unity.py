from __future__ import annotations

from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from models import MCPResponse
from services.registry import mcp_for_unity_tool
from services.tools import choose_unity_instance
import transport.unity_transport as unity_transport
from transport.legacy.unity_connection import async_send_command_with_retry
from services.state.external_changes_scanner import external_changes_scanner
import services.resources.editor_state as editor_state


@mcp_for_unity_tool(
    description="Request a Unity asset database refresh and optionally a script compilation. Can optionally wait for readiness.",
    annotations=ToolAnnotations(
        title="Refresh Unity",
        destructiveHint=True,
    ),
)
async def refresh_unity(
    ctx: Context,
    mode: Annotated[Literal["if_dirty", "force"], "Refresh mode"] = "if_dirty",
    scope: Annotated[Literal["assets", "scripts", "all"],
                     "Refresh scope"] = "all",
    compile: Annotated[Literal["none", "request"],
                       "Whether to request compilation"] = "none",
    wait_for_ready: Annotated[bool,
                              "If true, wait until editor_state.advice.ready_for_tools is true"] = True,
    unity_instance: Annotated[str,
                              "Optional target Unity instance Name@hash. Overrides the session active instance for this call."] | None = None,
) -> MCPResponse | dict[str, Any]:
    unity_instance = choose_unity_instance(ctx, unity_instance)

    params: dict[str, Any] = {
        "mode": mode,
        "scope": scope,
        "compile": compile,
        "wait_for_ready": bool(wait_for_ready),
    }

    response = await unity_transport.send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "refresh_unity",
        params,
    )

    if isinstance(response, dict) and not response.get("success", True):
        return MCPResponse(**response)

    # 只检查一次编辑器状态；失败后交给用户在 MCP 面板手动连接，避免后台轮询造成卡顿。
    if wait_for_ready:
        state_resp = await editor_state.get_editor_state_for_instance(ctx, unity_instance)
        state = state_resp.model_dump() if hasattr(
            state_resp, "model_dump") else state_resp
        data = (state or {}).get("data") if isinstance(
            state, dict) else None
        advice = (data or {}).get(
            "advice") if isinstance(data, dict) else None
        if not (isinstance(advice, dict) and advice.get("ready_for_tools") is True):
            return MCPResponse(
                success=False,
                error="Unity is not ready; please reconnect from the MCP panel",
                hint="manual_connect",
                data={"reason": "not_ready"},
            )

    # After readiness is restored, clear any external-dirty flag for this instance so future tools can proceed cleanly.
    try:
        inst = unity_instance or await editor_state.infer_single_instance_id(ctx)
        if inst:
            external_changes_scanner.clear_dirty(inst)
    except Exception:
        pass

    return MCPResponse(**response) if isinstance(response, dict) else response
