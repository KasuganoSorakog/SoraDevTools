"""Shared utilities used by all scrapers."""

import logging
import random
import time
from typing import Any, Dict, Optional, Tuple

import requests
from bs4 import BeautifulSoup

from config import BASE_URL, MAX_DELAY, MIN_DELAY, REQUEST_TIMEOUT

logger = logging.getLogger(__name__)


def polite_sleep() -> None:
    """Sleep a random amount between MIN_DELAY and MAX_DELAY seconds."""
    delay = random.uniform(MIN_DELAY, MAX_DELAY)
    logger.debug("Sleeping %.2fs …", delay)
    time.sleep(delay)


def get_json(
    session: requests.Session,
    endpoint: str,
    params: Dict[str, Any],
) -> Tuple[Optional[Dict], Optional[str]]:
    """GET request returning (json_data, raw_text).

    Returns (None, raw_text) when the response is not valid JSON.
    Returns (None, None) on HTTP error / redirect-to-login.
    """
    url = BASE_URL + endpoint
    try:
        resp = session.get(url, params=params, timeout=REQUEST_TIMEOUT, allow_redirects=False)
    except requests.RequestException as exc:
        logger.error("Request failed [%s]: %s", url, exc)
        return None, None

    if resp.status_code == 303:
        logger.warning("Session expired — HTTP 303 received.")
        return None, None

    if not resp.ok:
        logger.warning("HTTP %d for %s", resp.status_code, resp.url)
        return None, resp.text

    try:
        return resp.json(), resp.text
    except ValueError:
        logger.debug("Response is not JSON for %s, returning raw text.", endpoint)
        return None, resp.text


def parse_html(raw: str) -> BeautifulSoup:
    return BeautifulSoup(raw, "lxml")


def safe_int(value: Any, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def safe_str(value: Any, default: str = "") -> str:
    if value is None:
        return default
    return str(value).strip()
