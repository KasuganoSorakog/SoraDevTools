"""Authentication module for cbg_scraper.

Uses Playwright to open a real browser so the user can log in manually
(handles sliders, CAPTCHAs, etc.).  The resulting cookies are saved to
a local JSON file and injected into a requests.Session for subsequent
API calls.
"""

import json
import logging
import time
from pathlib import Path
from typing import Optional

import requests

from config import COOKIE_FILE, DEFAULT_HEADERS, LOGIN_TIMEOUT, LOGIN_URL

logger = logging.getLogger(__name__)

# A lightweight endpoint that requires login; returns non-303 when authenticated.
_CHECK_URL = "https://xyq.cbg.163.com/cgi-bin/recommend.py"


def login_and_save_cookies(cookie_file: str = COOKIE_FILE) -> None:
    """Open a browser, wait for the user to log in, then persist cookies.

    Requires `playwright` to be installed:
        pip install playwright && playwright install chromium
    """
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        raise RuntimeError(
            "Playwright is not installed. Run:\n"
            "  pip install playwright && playwright install chromium"
        )

    logger.info("Launching browser for manual login …")
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=False)
        context = browser.new_context()
        page = context.new_page()

        page.goto(LOGIN_URL)
        print(
            f"\n[auth] Browser opened. Please log in within {LOGIN_TIMEOUT} seconds.\n"
            "       Close the browser or press Ctrl-C when done.\n"
        )

        deadline = time.time() + LOGIN_TIMEOUT
        logged_in = False
        while time.time() < deadline:
            try:
                # Detect successful login: URL leaves the login domain or a
                # known auth cookie appears.
                current = page.url
                cookies = context.cookies()
                has_session = any(
                    c["name"] in ("u", "P_INFO", "NTES_SESS", "xyq_session")
                    for c in cookies
                )
                if has_session or "cbg.163.com" in current and "login" not in current.lower():
                    logged_in = True
                    break
            except Exception:
                pass
            time.sleep(2)

        if not logged_in:
            logger.warning("Login timeout — saving whatever cookies exist.")

        cookies = context.cookies()
        browser.close()

    if not cookies:
        raise RuntimeError("No cookies captured. Login may have failed.")

    Path(cookie_file).write_text(json.dumps(cookies, ensure_ascii=False, indent=2), encoding="utf-8")
    logger.info("Cookies saved to %s (%d entries).", cookie_file, len(cookies))
    print(f"[auth] Cookies saved to {cookie_file}.")


def load_session(cookie_file: str = COOKIE_FILE) -> requests.Session:
    """Load cookies from disk and return an authenticated requests.Session.

    Raises FileNotFoundError if the cookie file does not exist.
    Raises RuntimeError if the session appears to be invalid.
    """
    path = Path(cookie_file)
    if not path.exists():
        raise FileNotFoundError(
            f"Cookie file '{cookie_file}' not found. Run --login first."
        )

    cookies_data: list = json.loads(path.read_text(encoding="utf-8"))

    session = requests.Session()
    session.headers.update(DEFAULT_HEADERS)

    for c in cookies_data:
        session.cookies.set(
            c["name"],
            c["value"],
            domain=c.get("domain", ""),
            path=c.get("path", "/"),
        )

    _validate_session(session)
    return session


def _validate_session(session: requests.Session) -> None:
    """Probe the site to confirm the session is active.

    Raises RuntimeError if the response indicates the user is logged out.
    """
    try:
        resp = session.get(_CHECK_URL, timeout=10, allow_redirects=False)
    except requests.RequestException as exc:
        logger.warning("Session validation request failed: %s", exc)
        return  # network error — don't block; let scrapers handle it

    if resp.status_code == 303:
        raise RuntimeError(
            "Session is invalid (server returned 303 redirect). "
            "Run --login to refresh cookies."
        )
    logger.info("Session validated (HTTP %d).", resp.status_code)


def ensure_session(
    cookie_file: str = COOKIE_FILE,
    auto_relogin: bool = False,
) -> Optional[requests.Session]:
    """Return a valid session, optionally triggering re-login on failure."""
    try:
        return load_session(cookie_file)
    except (FileNotFoundError, RuntimeError) as exc:
        if auto_relogin:
            logger.info("Re-login triggered: %s", exc)
            login_and_save_cookies(cookie_file)
            return load_session(cookie_file)
        raise
