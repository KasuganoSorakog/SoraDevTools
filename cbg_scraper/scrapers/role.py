"""角色数据抓取器

接口: /cgi-bin/xyq_overall_search.py?act=show_search_role_form
"""

import json
import logging
from typing import Iterator, List, Optional

import requests

from models import Role
from scrapers._base import get_json, parse_html, polite_sleep, safe_int, safe_str
from storage import save_roles

logger = logging.getLogger(__name__)

_ENDPOINT = "xyq_overall_search.py"
_ACT = "show_search_role_form"


def _parse_role_json(data: dict) -> List[Role]:
    items = (
        data.get("data", {}).get("list")
        or data.get("list")
        or data.get("role_list")
        or []
    )
    roles: List[Role] = []
    for item in items:
        role_id = safe_str(item.get("game_ordersn") or item.get("ordersn") or item.get("id"))
        if not role_id:
            continue

        price_raw = item.get("price") or item.get("current_price") or 0
        try:
            price = int(float(price_raw) * 100) if isinstance(price_raw, float) else int(price_raw)
        except (TypeError, ValueError):
            price = 0

        # Store any unrecognised keys as extra_info JSON for future analysis
        known = {"game_ordersn", "ordersn", "id", "role_name", "name",
                 "role_level", "level", "school", "price", "current_price",
                 "area_name", "area", "server_name", "server", "seller_roleid", "role_id"}
        extra = {k: v for k, v in item.items() if k not in known}

        roles.append(Role(
            id=role_id,
            role_name=safe_str(item.get("role_name") or item.get("name")),
            role_level=safe_int(item.get("role_level") or item.get("level")),
            school=safe_str(item.get("school")),
            price=price,
            area_name=safe_str(item.get("area_name") or item.get("area")),
            server_name=safe_str(item.get("server_name") or item.get("server")),
            seller_roleid=safe_str(item.get("seller_roleid") or item.get("role_id")),
            extra_info=json.dumps(extra, ensure_ascii=False) if extra else "",
        ))
    return roles


def _parse_role_html(raw: str) -> List[Role]:
    soup = parse_html(raw)
    roles: List[Role] = []

    for item_div in soup.select(".sell-list-item, .role-item, .item-block"):
        role_id = (
            item_div.get("data-ordersn")
            or item_div.get("data-id")
            or item_div.get("id", "")
        )
        if not role_id:
            continue

        name_el = item_div.select_one(".role-name, .item-name, .name")
        level_el = item_div.select_one(".role-level, .level")
        school_el = item_div.select_one(".school, .role-school")
        price_el = item_div.select_one(".price, .item-price")

        price_text = safe_str(price_el.get_text() if price_el else "")
        try:
            price_fen = int(float("".join(c for c in price_text if c.isdigit() or c == ".")) * 100)
        except ValueError:
            price_fen = 0

        level_text = safe_str(level_el.get_text() if level_el else "")
        try:
            level = int("".join(c for c in level_text if c.isdigit()))
        except ValueError:
            level = 0

        roles.append(Role(
            id=str(role_id),
            role_name=safe_str(name_el.get_text() if name_el else ""),
            role_level=level,
            school=safe_str(school_el.get_text() if school_el else ""),
            price=price_fen,
        ))

    if not roles:
        logger.debug("HTML parser found no role items.")
    return roles


def scrape_roles(
    session: requests.Session,
    area_id: Optional[str] = None,
    server_id: Optional[str] = None,
    school: Optional[str] = None,
    min_level: Optional[int] = None,
    max_level: Optional[int] = None,
    sort_by: Optional[str] = None,
    page_size: int = 20,
    max_pages: int = 50,
    save: bool = True,
) -> Iterator[List[Role]]:
    """Paginated generator for role listings."""
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
        if school:
            params["school"] = school
        if min_level is not None:
            params["min_level"] = min_level
        if max_level is not None:
            params["max_level"] = max_level
        if sort_by:
            params["sort_by"] = sort_by

        logger.info("[role] Fetching page %d …", page)
        data, raw = get_json(session, _ENDPOINT, params)

        if data is None and raw is None:
            logger.warning("[role] No response on page %d — stopping.", page)
            break

        roles = _parse_role_json(data) if data is not None else []
        if not roles and raw:
            logger.debug("[role] Falling back to HTML parser.")
            roles = _parse_role_html(raw)

        if not roles:
            logger.info("[role] No items on page %d — reached end.", page)
            break

        if save:
            total_saved += save_roles(roles)

        yield roles
        polite_sleep()

    logger.info("[role] Done. Total records saved: %d", total_saved)
