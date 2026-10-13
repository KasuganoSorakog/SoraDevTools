import unittest
import inspect
import importlib
import sys
from types import SimpleNamespace
from unittest.mock import patch

for module_name in (
    "transport.plugin_hub",
    "transport.unity_transport",
    "transport.legacy.unity_connection",
    "core.config",
    "models.models",
):
    sys.modules.pop(module_name, None)
importlib.invalidate_caches()

from services.tools.execute_menu_item import execute_menu_item
from services.tools.find_gameobjects import find_gameobjects
from services.tools.find_in_file import find_in_file
from services.tools.manage_asset import manage_asset
from services.tools.manage_components import manage_components
from services.tools.manage_gameobject import manage_gameobject
from services.tools.manage_material import manage_material
from services.tools.manage_prefabs import manage_prefabs
from services.tools.manage_scene import manage_scene
from services.tools.manage_script import (
    apply_text_edits,
    create_script,
    delete_script,
    get_sha,
    manage_script,
    validate_script,
)
from services.tools.manage_scriptable_object import manage_scriptable_object
from services.tools.manage_shader import manage_shader
from services.tools.manage_vfx import manage_vfx
from services.tools.execute_custom_tool import execute_custom_tool
from services.tools.run_tests import get_test_job, run_tests
from services.tools.script_apply_edits import script_apply_edits


ROUTED_TOOLS = [
    execute_custom_tool,
    execute_menu_item,
    find_gameobjects,
    find_in_file,
    manage_asset,
    manage_components,
    manage_gameobject,
    manage_material,
    manage_prefabs,
    manage_scene,
    manage_scriptable_object,
    manage_shader,
    manage_vfx,
    run_tests,
    get_test_job,
    apply_text_edits,
    create_script,
    delete_script,
    validate_script,
    manage_script,
    get_sha,
    script_apply_edits,
]


class FakeContext(SimpleNamespace):
    def __init__(self, unity_instance: str | None):
        super().__init__(state={"unity_instance": unity_instance}, infos=[])

    def get_state(self, key):
        return self.state.get(key)

    async def info(self, message):
        self.infos.append(message)

    async def error(self, message):
        self.infos.append(message)


class ToolUnityInstanceRoutingTests(unittest.IsolatedAsyncioTestCase):
    def test_routed_tools_expose_optional_tail_unity_instance_parameter(self):
        for tool in ROUTED_TOOLS:
            with self.subTest(tool=tool.__name__):
                parameters = list(inspect.signature(tool).parameters.values())
                unity_param = parameters[-1]

                self.assertEqual(unity_param.name, "unity_instance")
                self.assertIsNone(unity_param.default)

    async def test_explicit_unity_instance_overrides_context_and_stays_out_of_unity_params(self):
        captured = {}

        async def fake_send(send_fn, unity_instance, command_type, params, **kwargs):
            captured["unity_instance"] = unity_instance
            captured["command_type"] = command_type
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {}}

        ctx = FakeContext("ContextProject@ctx-hash")
        with patch("services.tools.manage_asset.send_with_unity_instance", fake_send):
            result = await manage_asset(
                ctx,
                action="search",
                path="Assets",
                unity_instance="ExplicitProject@explicit-hash",
            )

        self.assertTrue(result["success"])
        self.assertEqual(captured["unity_instance"], "ExplicitProject@explicit-hash")
        self.assertEqual(captured["command_type"], "manage_asset")
        self.assertNotIn("unity_instance", captured["params"])
        self.assertNotIn("unityInstance", captured["params"])

    async def test_context_unity_instance_is_used_when_explicit_target_is_absent(self):
        captured = {}

        async def fake_send(send_fn, unity_instance, command_type, params, **kwargs):
            captured["unity_instance"] = unity_instance
            captured["command_type"] = command_type
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {}}

        ctx = FakeContext("ContextProject@ctx-hash")
        with patch("services.tools.manage_gameobject.send_with_unity_instance", fake_send):
            result = await manage_gameobject(
                ctx,
                action="create",
                name="Probe",
            )

        self.assertTrue(result["success"])
        self.assertEqual(captured["unity_instance"], "ContextProject@ctx-hash")
        self.assertEqual(captured["command_type"], "manage_gameobject")
        self.assertNotIn("unity_instance", captured["params"])
        self.assertNotIn("unityInstance", captured["params"])

    async def test_execute_custom_tool_does_not_pollute_custom_parameters(self):
        captured = {}

        class FakeCustomToolService:
            async def execute_tool(self, project_id, tool_name, unity_instance, parameters):
                captured["project_id"] = project_id
                captured["tool_name"] = tool_name
                captured["unity_instance"] = unity_instance
                captured["parameters"] = parameters
                return {"success": True, "message": "ok"}

        ctx = FakeContext("ContextProject@ctx-hash")
        with (
            patch("services.tools.execute_custom_tool.resolve_project_id_for_unity_instance", return_value="project-1"),
            patch("services.tools.execute_custom_tool.CustomToolService.get_instance", return_value=FakeCustomToolService()),
        ):
            result = await execute_custom_tool(
                ctx,
                tool_name="CustomProbe",
                parameters={"value": 7},
                unity_instance="ExplicitProject@explicit-hash",
            )

        self.assertTrue(result["success"])
        self.assertEqual(captured["project_id"], "project-1")
        self.assertEqual(captured["tool_name"], "CustomProbe")
        self.assertEqual(captured["unity_instance"], "ExplicitProject@explicit-hash")
        self.assertEqual(captured["parameters"], {"value": 7})


if __name__ == "__main__":
    unittest.main()
