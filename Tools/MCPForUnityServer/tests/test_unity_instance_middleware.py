import asyncio
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
    configured = False

    @classmethod
    def is_configured(cls):
        return cls.configured

    @classmethod
    async def _resolve_session_id(cls, unity_instance):
        raise RuntimeError("not connected")


plugin_hub_module.PluginHub = PluginHub
sys.modules["transport.plugin_hub"] = plugin_hub_module

import transport.unity_instance_middleware as unity_instance_middleware

from transport.unity_instance_middleware import UnityInstanceMiddleware


def make_codex_context(thread_id, workspace_root="E:/Project", server_name="UnityMCP"):
    return SimpleNamespace(
        client_id=None,
        session_id=None,
        request_context=SimpleNamespace(
            client_id=None,
            session_id="shared-fastmcp-session",
            meta={
                "x-codex-turn-metadata": {
                    "thread_id": thread_id,
                    "workspaces": [{"root": workspace_root}],
                    "mcp_server": server_name,
                }
            },
        ),
    )


class UnityInstanceMiddlewareTests(unittest.TestCase):
    def test_active_instances_are_isolated_between_codex_threads(self):
        middleware = UnityInstanceMiddleware()
        ctx_a = make_codex_context("thread-a")
        ctx_b = make_codex_context("thread-b")

        middleware.set_active_instance(ctx_a, "ProjectA@aaa111")
        middleware.set_active_instance(ctx_b, "ProjectB@bbb222")

        self.assertEqual(middleware.get_active_instance(ctx_a), "ProjectA@aaa111")
        self.assertEqual(middleware.get_active_instance(ctx_b), "ProjectB@bbb222")

    def test_clear_active_instance_only_clears_current_session_key(self):
        middleware = UnityInstanceMiddleware()
        ctx_a = make_codex_context("thread-a")
        ctx_b = make_codex_context("thread-b")

        middleware.set_active_instance(ctx_a, "ProjectA@aaa111")
        middleware.set_active_instance(ctx_b, "ProjectB@bbb222")
        middleware.clear_active_instance(ctx_a)

        self.assertIsNone(middleware.get_active_instance(ctx_a))
        self.assertEqual(middleware.get_active_instance(ctx_b), "ProjectB@bbb222")

    def test_active_instance_survives_tool_resource_identity_shape_changes(self):
        middleware = UnityInstanceMiddleware()
        tool_ctx = make_codex_context(
            "thread-a",
            workspace_root="E:/Project",
            server_name="UnityMCP_8080",
        )
        resource_ctx = SimpleNamespace(
            client_id=None,
            session_id=None,
            request_context=SimpleNamespace(
                client_id=None,
                session_id="different-fastmcp-session",
                meta={
                    "x-codex-turn-metadata": {
                        "thread_id": "thread-a",
                    }
                },
            ),
        )

        middleware.set_active_instance(tool_ctx, "ProjectA@aaa111")

        self.assertEqual(
            middleware.get_active_instance(resource_ctx),
            "ProjectA@aaa111",
        )

    def test_set_active_instance_rejects_unkeyed_context(self):
        middleware = UnityInstanceMiddleware()
        ctx = SimpleNamespace(client_id=None, session_id=None, request_context=None)

        with self.assertRaisesRegex(ValueError, "stable MCP client session key"):
            middleware.set_active_instance(ctx, "ProjectA@aaa111")

    def test_unreachable_active_instance_is_cleared_before_autoselect(self):
        class ContextState(SimpleNamespace):
            def __init__(self, **kwargs):
                super().__init__(**kwargs)
                self.state = {}

            def set_state(self, key, value):
                self.state[key] = value

        class MiddlewareContext:
            def __init__(self, fastmcp_context):
                self.fastmcp_context = fastmcp_context

        async def run_test():
            middleware = UnityInstanceMiddleware()
            ctx = ContextState(
                client_id="client-a",
                session_id=None,
                request_context=None,
            )
            middleware.set_active_instance(ctx, "ProjectA@aaa111")
            old_plugin_hub = unity_instance_middleware.PluginHub
            unity_instance_middleware.PluginHub = PluginHub
            PluginHub.configured = True
            try:
                await middleware._inject_unity_instance(MiddlewareContext(ctx))
            finally:
                PluginHub.configured = False
                unity_instance_middleware.PluginHub = old_plugin_hub

            self.assertIsNone(middleware.get_active_instance(ctx))
            self.assertNotIn("unity_instance", ctx.state)

        asyncio.run(run_test())


if __name__ == "__main__":
    unittest.main()
