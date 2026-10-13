"""Utilities for normalizing Unity transport responses."""
from __future__ import annotations

from typing import Any


def normalize_unity_response(response: Any) -> Any:
    """Normalize Unity's {status,result} payloads into MCPResponse shape."""
    if not isinstance(response, dict):
        return response

    status = response.get("status")
    result = response.get("result") if isinstance(
        response.get("result"), dict) else response.get("result")

    # Already MCPResponse-shaped
    if "success" in response:
        return _with_failure_classification(response)
    if isinstance(result, dict) and "success" in result:
        return _with_failure_classification(result)

    if status is None:
        return response

    payload = result if isinstance(result, dict) else {}
    success = status == "success"
    message = payload.get("message") or response.get("message")
    error = payload.get("error") or response.get("error")

    data = payload.get("data")
    if data is None and isinstance(payload, dict) and payload:
        data = {k: v for k, v in payload.items() if k not in {
            "message", "error", "status", "code"}}
        if not data:
            data = None

    normalized: dict[str, Any] = {
        "success": success,
        "message": message,
        "error": error if not success else None,
        "data": data,
    }

    for key in ("code", "category", "severity", "retryable", "retry_after_ms", "hint"):
        value = payload.get(key) if isinstance(payload, dict) else None
        if value is None:
            value = response.get(key)
        if value is not None:
            normalized[key] = value

    if not success and not normalized["error"]:
        normalized["error"] = message or "Unity command failed"

    return _with_failure_classification(normalized)


def _with_failure_classification(response: dict[str, Any]) -> dict[str, Any]:
    """Fill stable error fields for common legacy Unity retry payloads."""

    if response.get("success") is not False:
        return response

    error = response.get("error")
    data = response.get("data") if isinstance(response.get("data"), dict) else {}
    reason = data.get("reason") or response.get("message") or error

    if error == "busy" or response.get("hint") == "retry":
        reason_text = _slug_reason(reason or "busy")
        response.setdefault("code", f"unity_busy_{reason_text}")
        response.setdefault("category", "busy")
        response.setdefault("severity", "warning")
        response.setdefault("retryable", True)
        if response.get("retry_after_ms") is None and data.get("retry_after_ms") is not None:
            try:
                response["retry_after_ms"] = int(data["retry_after_ms"])
            except Exception:
                pass
        response.setdefault("hint", "retry")

    return response


def _slug_reason(reason: Any) -> str:
    text = str(reason or "busy").strip().lower()
    chars = [ch if ch.isalnum() else "_" for ch in text]
    slug = "_".join(part for part in "".join(chars).split("_") if part)
    return slug or "busy"
