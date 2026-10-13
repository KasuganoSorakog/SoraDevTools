"""装备数据抓取器

接口: /cgi-bin/equipquery.py?act=show_overall_search_equip

响应格式：可能是 JSON 或 HTML 模板渲染（两种都处理）。
"""

import json
import logging
from typing import Iterator, List, Optional

import requests

from models import Equip
from scrapers._base import get_json, parse_html, polite_sleep, safe_int, safe_str
from storage import save_equips

logger = logging.getLogger(__name__)

_ENDPOINT = "equipquery.py"
_ACT = "show_overall_search_equip"


def _parse_equip_json(data: dict) -> List[Equip]:
    """Parse a JSON response from the equip search endpoint."""
    equips: List[Equip] = []

    # The response structure is not fully confirmed; try common shapes.
    items = (
        data.get("data", {}).get("list")
        or data.get("list")
        or data.get("equip_list")
        or []
    )

    for item in items:
        equip_id = safe_str(item.get("game_ordersn") or item.get("ordersn") or item.get("id"))
        if not equip_id:
            logger.debug("Skipping item with no ID: %s", item)
            continue

        price_raw = item.get("price") or item.get("current_price") or 0
        # Price may arrive in yuan (float) or fen (int); normalise to fen.
        try:
            price = int(float(price_raw) * 100) if isinstance(price_raw, float) else int(price_raw)
        except (TypeError, ValueError):
            price = 0

        equips.append(Equip(
            id=equip_id,
            equip_name=safe_str(item.get("equip_name") or item.get("name")),
            equip_level=safe_int(item.get("equip_level") or item.get("level")),
            price=price,
            init_damage=safe_int(item.get("init_damage") or item.get("attack")),
            init_defense=safe_int(item.get("init_defense") or item.get("defense")),
            init_hp=safe_int(item.get("init_hp") or item.get("hp")),
            init_dex=safe_int(item.get("init_dex") or item.get("dex") or item.get("speed")),
            gem_level=safe_int(item.get("gem_level")),
            special_effect=safe_str(item.get("special_effect") or item.get("effect")),
            equip_type=safe_str(item.get("equip_type") or item.get("type")),
            area_name=safe_str(item.get("area_name") or item.get("area")),
            server_name=safe_str(item.get("server_name") or item.get("server")),
            seller_roleid=safe_str(item.get("seller_roleid") or item.get("role_id")),
            is_locked=bool(item.get("is_lock") or item.get("locked")),
        ))

    return equips


def _parse_equip_html(raw: str) -> List[Equip]:
    """Fallback HTML parser for equip listings."""
    soup = parse_html(raw)
    equips: List[Equip] = []

    # Each item is typically inside a <div class="item"> or similar container.
    # Without confirmed markup we attempt a generic approach.
    for item_div in soup.select(".sell-list-item, .equip-item, .item-block"):
        equip_id = (
            item_div.get("data-ordersn")
            or item_div.get("data-id")
            or item_div.get("id", "")
        )
        if not equip_id:
            continue

        name_el = item_div.select_one(".equip-name, .item-name, .name")
        price_el = item_div.select_one(".price, .item-price")
        server_el = item_div.select_one(".server-name, .server")

        price_text = safe_str(price_el.get_text() if price_el else "")
        try:
            price_fen = int(float("".join(c for c in price_text if c.isdigit() or c == ".")) * 100)
        except ValueError:
            price_fen = 0

        equips.append(Equip(
            id=str(equip_id),
            equip_name=safe_str(name_el.get_text() if name_el else ""),
            price=price_fen,
            server_name=safe_str(server_el.get_text() if server_el else ""),
        ))

    if not equips:
        logger.debug("HTML parser found no equip items (markup may have changed).")
    return equips


def scrape_equips(
    session: requests.Session,
    area_id: Optional[str] = None,
    server_id: Optional[str] = None,
    equip_type: Optional[str] = None,
    page_size: int = 20,
    max_pages: int = 50,
    save: bool = True,
) -> Iterator[List[Equip]]:
    """Paginated generator: yields one list of Equip per page.

    Args:
        session:    Authenticated requests.Session.
        area_id:    大区 ID（可选）.
        server_id:  服务器 ID（可选）.
        equip_type: 装备类型代码（可选）.
        page_size:  每页数量.
        max_pages:  最多抓取页数（保护措施）.
        save:       是否自动写入 SQLite.
    """
    total_saved = 0

    for page in range(1, max_pages + 1):
        params: dict = {
            "act": _ACT,
            "page": page,
            "pagesize": page_size,
            "client_type": "web",
        }
        if area_id:
            params["area_id"] = area_id
        if server_id:
            params["server_id"] = server_id
        if equip_type:
            params["equip_type"] = equip_type

        logger.info("[equip] Fetching page %d …", page)
        data, raw = get_json(session, _ENDPOINT, params)

        if data is None and raw is None:
            logger.warning("[equip] No response on page %d — stopping.", page)
            break

        if data is not None:
            equips = _parse_equip_json(data)
        elif raw:
            logger.debug("[equip] Falling back to HTML parser for page %d.", page)
            equips = _parse_equip_html(raw)
        else:
            equips = []

        if not equips:
            logger.info("[equip] No items on page %d — reached end.", page)
            break

        if save:
            total_saved += save_equips(equips)

        yield equips

        polite_sleep()

    logger.info("[equip] Done. Total records saved: %d", total_saved)
