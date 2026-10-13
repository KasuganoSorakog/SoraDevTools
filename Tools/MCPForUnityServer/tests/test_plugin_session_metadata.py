import asyncio
import sys
import unittest
from types import ModuleType


models_models = ModuleType("models.models")


class ToolDefinitionModel:
    def __init__(self, name):
        self.name = name


models_models.ToolDefinitionModel = ToolDefinitionModel
sys.modules["models.models"] = models_models

from transport.plugin_registry import PluginRegistry


class PluginSessionMetadataTests(unittest.IsolatedAsyncioTestCase):
    async def test_register_stores_extended_project_metadata(self):
        registry = PluginRegistry()

        session = await registry.register(
            "session-1",
            "ProjectA",
            "abc123",
            "2022.3.56f1",
            project_path="E:/ProjectA",
            package_version="9.0.3",
            current_scene="Assets/Main.unity",
            capabilities_version="1",
        )

        self.assertEqual(session.project_path, "E:/ProjectA")
        self.assertEqual(session.package_version, "9.0.3")
        self.assertEqual(session.current_scene, "Assets/Main.unity")
        self.assertEqual(session.capabilities_version, "1")

        sessions = await registry.list_sessions()
        self.assertEqual(sessions["session-1"].project_path, "E:/ProjectA")


if __name__ == "__main__":
    unittest.main()
