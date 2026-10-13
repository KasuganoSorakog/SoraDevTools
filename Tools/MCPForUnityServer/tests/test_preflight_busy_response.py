import sys
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

for module_name in ("fastmcp", "fastmcp.server", "fastmcp.server.middleware"):
    sys.modules.pop(module_name, None)

from services.tools.preflight import _busy


class PreflightBusyResponseTests(unittest.TestCase):
    def test_preflight_busy_response_is_machine_readable(self):
        response = _busy("compiling", 500).model_dump()

        self.assertFalse(response["success"])
        self.assertEqual(response["code"], "unity_busy_compiling")
        self.assertEqual(response["category"], "busy")
        self.assertEqual(response["severity"], "warning")
        self.assertTrue(response["retryable"])
        self.assertEqual(response["retry_after_ms"], 500)
        self.assertEqual(response["hint"], "retry")


if __name__ == "__main__":
    unittest.main()
