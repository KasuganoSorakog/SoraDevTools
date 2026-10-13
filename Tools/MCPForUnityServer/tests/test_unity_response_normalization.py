import unittest

from models.unity_response import normalize_unity_response


class UnityResponseNormalizationTests(unittest.TestCase):
    def test_normalizes_legacy_busy_payload(self):
        response = normalize_unity_response(
            {
                "success": False,
                "message": "compiling",
                "error": "busy",
                "data": {"reason": "compiling", "retry_after_ms": 500},
                "hint": "retry",
            }
        )

        self.assertEqual(response["code"], "unity_busy_compiling")
        self.assertEqual(response["category"], "busy")
        self.assertTrue(response["retryable"])
        self.assertEqual(response["retry_after_ms"], 500)

    def test_normalizes_status_error_payload(self):
        response = normalize_unity_response(
            {
                "status": "error",
                "result": {
                    "message": "Reloading domain",
                    "error": "busy",
                    "data": {"reason": "domain_reload", "retry_after_ms": 750},
                },
            }
        )

        self.assertFalse(response["success"])
        self.assertEqual(response["code"], "unity_busy_domain_reload")
        self.assertEqual(response["category"], "busy")
        self.assertEqual(response["retry_after_ms"], 750)


if __name__ == "__main__":
    unittest.main()
