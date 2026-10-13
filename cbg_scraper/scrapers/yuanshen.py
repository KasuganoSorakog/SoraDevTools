"""元神数据抓取器

接口: /cgi-bin/query.py?act=recommend_search  (recommend_type=yuanshen 或类似参数)
"""

import json
import logging
from typing import Iterator, List, Optional

import requests

from models import Yuanshen
from scrapers._base import get_json, parse_html, polite_sleep, safe_int, safe_str
from storage import save_yuanshen

logger = logging.getLogger(__name__)

_ENDPOINT = "query.py"
_ACT = "recommend_search"
_RECOMMEND_TYPE = "yuanshen"


def _parse_yuanshen_json(data: dict) -> List[Yuanshen]:
    items = (
        data.get("data", {}).get("list")
        or data.get("list")
        or data.get("yuanshen_list")
        or []
    )
    results: List[Yuanshen] = []
    for item in items:
        ys_id = safe_str(item.get("game_ordersn") or item.get("ordersn") or item.get("id"))
        if not ys_id:
            continue

        price_raw = item.get("price") or item.get("current_price") or 0
        try:
            price = int(float(price_raw) * 100) if isinstance(price_raw, float) else int(price_raw)
        except (TypeError, ValueError):
            price = 0

        # Collect numeric/string attributes into a JSON blob
        known = {"game_ordersn", "ordersn", "id", "yuanshen_name", "name",
                 "level", "quality", "grade", "price", "current_price",
                 "area_name", "area", "server_name", "server", "seller_roleid", "role_id"}
        attrs = {k: v for k, v in item.items() if k not in known}

        results.append(Yuanshen(
            id=ys_id,
            yuanshen_name=safe_str(item.get("yuanshen_name") or item.get("name")),
            level=safe_int(item.get("level")),
            quality=safe_str(item.get("quality") or item.get("grade")),
            attributes=json.dumps(attrs, ensure_ascii=False) if attrs else "",
            price=price,
            area_name=safe_str(item.get("area_name") or item.get("area")),
            server_name=safe_str(item.get("server_name") or item.get("server")),
            seller_roleid=safe_str(item.get("seller_roleid") or item.get("role_id")),
        ))
    return results


def _parse_yuanshen_html(raw: str) -> List[Yuanshen]:
    soup = parse_html(raw)
    results: List[Yuanshen] = []
    for item_div in soup.select(".sell-list-item, .yuanshen-item, .item-block"):
        ys_id = (
            item_div.get("data-ordersn")
            or item_div.get("data-id")
            or item_div.get("id", "")
        )
        if not ys_id:
            continue
        name_el = item_div.select_one(".yuanshen-name, .item-name, .name")
        price_el = item_div.select_one(".price, .item-price")
        price_text = safe_str(price_el.get_text() if price_el else "")
        try:
            price_fen = int(float("".join(c for c in price_text if c.isdigit() or c == ".")) * 100)
        except ValueError:
            price_fen = 0
        results.append(Yuanshen(
            id=str(ys_id),
            yuanshen_name=safe_str(name_el.get_text() if name_el else ""),
            price=price_fen,
        ))
    if not results:
        logger.debug("HTML parser found no yuanshen items.")
    return results


def scrape_yuanshen(
    session: requests.Session,
    area_id: Optional[str] = None,
    server_id: Optional[str] = None,
    page_size: int = 20,
    max_pages: int = 50,
    save: bool = True,
) -> Iterator[List[Yuanshen]]:
    """Paginated generator for yuanshen listings."""
    total_saved = 0

    for page in range(1, max_pages + 1):
        params: dict = {
            "act": _ACT,
            "recommend_type": _RECOMMEND_TYPE,
            "page": page,
            "pagesize": page_size,
            "client_type": "web",
        }
        if area_id:
            params["area_id"] = area_id
        if server_id:
            params["server_id"] = server_id

        logger.info("[yuanshen] Fetching page %d …", page)
        data, raw = get_json(session, _ENDPOINT, params)

        if data is None and raw is None:
            logger.warning("[yuanshen] No response on page %d — stopping.", page)
            break

        items = _parse_yuanshen_json(data) if data is not None else []
        if not items and raw:
            logger.debug("[yuanshen] Falling back to HTML parser.")
            items = _parse_yuanshen_html(raw)

        if not items:
            logger.info("[yuanshen] No items on page %d — reached end.", page)
            break

        if save:
            total_saved += save_yuanshen(items)

        yield items
        polite_sleep()

    logger.info("[yuanshen] Done. Total records saved: %d", total_saved)
