import sys
import unittest
from pathlib import Path
from types import ModuleType


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

core_config = ModuleType("core.config")
core_config.config = type("Config", (), {"reload_retry_ms": 250})()
sys.modules["core.config"] = core_config

models_models = ModuleType("models.models")
from pydantic import BaseModel


class MCPResponse(BaseModel):
    success: bool = True
    error: str | None = None
    hint: str | None = None
    data: dict | None = None


class ToolDefinitionModel(BaseModel):
    name: str


models_models.MCPResponse = MCPResponse
models_models.ToolDefinitionModel = ToolDefinitionModel
sys.modules["models.models"] = models_models

from transport.plugin_hub import PluginHub


class PluginHubTimeoutTests(unittest.TestCase):
    def test_read_console_is_not_a_two_second_fast_fail_command(self):
        self.assertNotIn("read_console", PluginHub._FAST_FAIL_COMMANDS)

    def test_read_console_has_a_dedicated_timeout_budget(self):
        self.assertGreaterEqual(PluginHub.READ_CONSOLE_TIMEOUT, 10.0)


if __name__ == "__main__":
    unittest.main()
