# Configuration for cbg_scraper

BASE_URL = "https://xyq.cbg.163.com/cgi-bin/"

# Login page — may need to switch to xyq.cbg.163.com after verification
LOGIN_URL = "https://cbg.163.com"

COOKIE_FILE = "cookies.json"
DB_FILE = "cbg_data.db"

# HTTP settings
REQUEST_TIMEOUT = 10  # seconds

# Polite delay between requests (seconds)
MIN_DELAY = 1.5
MAX_DELAY = 3.0

# Pagination
DEFAULT_PAGE_SIZE = 20
DEFAULT_MAX_PAGES = 50

DEFAULT_HEADERS = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        "AppleWebKit/537.36 (KHTML, like Gecko) "
        "Chrome/120.0.0.0 Safari/537.36"
    ),
    "Referer": "https://xyq.cbg.163.com/",
    "Accept": "application/json, text/html, */*",
    "Accept-Language": "zh-CN,zh;q=0.9",
    "Accept-Encoding": "gzip, deflate, br",
}

# Playwright login timeout (seconds) — user has this long to log in manually
LOGIN_TIMEOUT = 180
