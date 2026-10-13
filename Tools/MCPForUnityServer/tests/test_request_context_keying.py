import sys
import unittest
from types import ModuleType, SimpleNamespace


fastmcp_middleware = ModuleType("fastmcp.server.middleware")


class Middleware:
    pass


class MiddlewareContext:
    pass


fastmcp_middleware.Middleware = Middleware
fastmcp_middleware.MiddlewareContext = MiddlewareContext
sys.modules.setdefault("fastmcp", ModuleType("fastmcp"))
sys.modules.setdefault("fastmcp.server", ModuleType("fastmcp.server"))
sys.modules["fastmcp.server.middleware"] = fastmcp_middleware

plugin_hub_module = ModuleType("transport.plugin_hub")


class PluginHub:
    @classmethod
    def is_configured(cls):
        return False


plugin_hub_module.PluginHub = PluginHub
sys.modules["transport.plugin_hub"] = plugin_hub_module

from transport.unity_instance_middleware import (
    UnityInstanceMiddleware,
    extract_request_identity,
)


class ModelDumpMeta:
    def __init__(self, payload):
        self.payload = payload

    def model_dump(self, exclude_none=False):
        return self.payload


def make_context(*, client_id=None, session_id=None, request_context=None):
    return SimpleNamespace(
        client_id=client_id,
        session_id=session_id,
        request_context=request_context,
    )


class RequestContextKeyingTests(unittest.TestCase):
    def test_session_key_prefers_direct_client_id(self):
        ctx = make_context(client_id="codex-client-1", session_id="session-a")
        middleware = UnityInstanceMiddleware()

        self.assertEqual(middleware.get_session_key(ctx), "client:codex-client-1")

    def test_session_key_uses_codex_thread_workspace_and_server_metadata_without_client_id(self):
        meta = ModelDumpMeta(
            {
                "x-codex-turn-metadata": {
                    "thread_id": "thread-123",
                    "workspaces": [
                        {
                            "root": "E:/YueMaHuChenVR",
                            "name": "YueMaHuChenVR",
                        }
                    ],
                    "mcp_server": "UnityMCP_8080",
                }
            }
        )
        request_context = SimpleNamespace(
            client_id=None,
            session_id="fastmcp-session-a",
            meta=meta,
        )
        ctx = make_context(request_context=request_context)
        middleware = UnityInstanceMiddleware()

        self.assertEqual(
            middleware.get_session_key(ctx),
            "codex:thread-123|workspace:E:/YueMaHuChenVR|server:UnityMCP_8080",
        )

    def test_codex_metadata_is_not_collapsed_to_reused_client_id(self):
        meta = {
            "x-codex-turn-metadata": {
                "thread_id": "thread-123",
                "workspaces": [{"root": "E:/ProjectA"}],
                "mcp_server": "UnityMCP_8080",
            }
        }
        request_context = SimpleNamespace(
            client_id="shared-client",
            session_id="fastmcp-session-a",
            meta=meta,
        )
        ctx = make_context(client_id="shared-client", request_context=request_context)
        middleware = UnityInstanceMiddleware()

        self.assertEqual(
            middleware.get_session_key(ctx),
            "codex:thread-123|client:shared-client|workspace:E:/ProjectA|server:UnityMCP_8080",
        )

    def test_session_key_uses_fastmcp_session_when_codex_metadata_is_absent(self):
        request_context = SimpleNamespace(
            client_id=None,
            session_id="fastmcp-session-a",
            meta={"trace": "no codex metadata"},
        )
        ctx = make_context(session_id=None, request_context=request_context)
        middleware = UnityInstanceMiddleware()

        self.assertEqual(middleware.get_session_key(ctx), "session:fastmcp-session-a")

    def test_session_key_never_falls_back_to_global_for_unknown_context(self):
        middleware = UnityInstanceMiddleware()

        self.assertIsNone(middleware.get_session_key(make_context()))

    def test_extract_request_identity_reports_key_inputs_for_diagnostics(self):
        meta = {
            "x-codex-turn-metadata": {
                "thread_id": "thread-456",
                "workspaces": [{"root": "E:/ProjectA"}],
                "mcp_server": "UnityMCP",
            }
        }
        request_context = SimpleNamespace(
            client_id="request-client",
            session_id="request-session",
            meta=meta,
        )
        ctx = make_context(
            client_id=None,
            session_id="direct-session",
            request_context=request_context,
        )

        identity = extract_request_identity(ctx)

        self.assertEqual(identity["client_id"], "request-client")
        self.assertEqual(identity["session_id"], "direct-session")
        self.assertEqual(identity["request_context_session_id"], "request-session")
        self.assertEqual(identity["codex_thread_id"], "thread-456")
        self.assertEqual(identity["codex_workspace_roots"], ["E:/ProjectA"])
        self.assertEqual(identity["server_name"], "UnityMCP")


if __name__ == "__main__":
    unittest.main()
