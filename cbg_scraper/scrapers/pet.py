"""宠物数据抓取器

接口: /cgi-bin/query.py?act=recommend_search  (recommend_type=pet 或类似参数)
"""

import json
import logging
from typing import Iterator, List, Optional

import requests

from models import Pet
from scrapers._base import get_json, parse_html, polite_sleep, safe_int, safe_str
from storage import save_pets

logger = logging.getLogger(__name__)

_ENDPOINT = "query.py"
_ACT = "recommend_search"
_RECOMMEND_TYPE = "pet"


def _parse_pet_json(data: dict) -> List[Pet]:
    items = (
        data.get("data", {}).get("list")
        or data.get("list")
        or data.get("pet_list")
        or []
    )
    pets: List[Pet] = []
    for item in items:
        pet_id = safe_str(item.get("game_ordersn") or item.get("ordersn") or item.get("id"))
        if not pet_id:
            continue

        price_raw = item.get("price") or item.get("current_price") or 0
        try:
            price = int(float(price_raw) * 100) if isinstance(price_raw, float) else int(price_raw)
        except (TypeError, ValueError):
            price = 0

        # Skills can be a list or comma-separated string
        skills_raw = item.get("skills") or item.get("skill_list") or []
        if isinstance(skills_raw, list):
            skills_str = ",".join(str(s) for s in skills_raw)
        else:
            skills_str = safe_str(skills_raw)

        evolve_raw = item.get("evolve_skills") or item.get("evolve_skill_list") or []
        if isinstance(evolve_raw, list):
            evolve_str = ",".join(str(s) for s in evolve_raw)
        else:
            evolve_str = safe_str(evolve_raw)

        pets.append(Pet(
            id=pet_id,
            pet_name=safe_str(item.get("pet_name") or item.get("name")),
            pet_type=safe_str(item.get("pet_type") or item.get("type")),
            level=safe_int(item.get("level") or item.get("pet_level")),
            quality=safe_str(item.get("quality") or item.get("grade")),
            skills=skills_str,
            evolve_skills=evolve_str,
            price=price,
            area_name=safe_str(item.get("area_name") or item.get("area")),
            server_name=safe_str(item.get("server_name") or item.get("server")),
            seller_roleid=safe_str(item.get("seller_roleid") or item.get("role_id")),
        ))
    return pets


def _parse_pet_html(raw: str) -> List[Pet]:
    soup = parse_html(raw)
    pets: List[Pet] = []
    for item_div in soup.select(".sell-list-item, .pet-item, .item-block"):
        pet_id = (
            item_div.get("data-ordersn")
            or item_div.get("data-id")
            or item_div.get("id", "")
        )
        if not pet_id:
            continue
        name_el = item_div.select_one(".pet-name, .item-name, .name")
        price_el = item_div.select_one(".price, .item-price")
        price_text = safe_str(price_el.get_text() if price_el else "")
        try:
            price_fen = int(float("".join(c for c in price_text if c.isdigit() or c == ".")) * 100)
        except ValueError:
            price_fen = 0
        pets.append(Pet(
            id=str(pet_id),
            pet_name=safe_str(name_el.get_text() if name_el else ""),
            price=price_fen,
        ))
    if not pets:
        logger.debug("HTML parser found no pet items.")
    return pets


def scrape_pets(
    session: requests.Session,
    area_id: Optional[str] = None,
    server_id: Optional[str] = None,
    pet_type: Optional[str] = None,
    page_size: int = 20,
    max_pages: int = 50,
    save: bool = True,
) -> Iterator[List[Pet]]:
    """Paginated generator for pet listings."""
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
        if pet_type:
            params["pet_type"] = pet_type

        logger.info("[pet] Fetching page %d …", page)
        data, raw = get_json(session, _ENDPOINT, params)

        if data is None and raw is None:
            logger.warning("[pet] No response on page %d — stopping.", page)
            break

        pets = _parse_pet_json(data) if data is not None else []
        if not pets and raw:
            logger.debug("[pet] Falling back to HTML parser.")
            pets = _parse_pet_html(raw)

        if not pets:
            logger.info("[pet] No items on page %d — reached end.", page)
            break

        if save:
            total_saved += save_pets(pets)

        yield pets
        polite_sleep()

    logger.info("[pet] Done. Total records saved: %d", total_saved)
