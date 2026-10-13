#!/usr/bin/env python3
"""cbg_scraper — 梦幻西游藏宝阁数据抓取工具

Usage examples:
    python main.py --login                          # 重新登录，刷新 Cookie
    python main.py --type equip --page-limit 2      # 抓取前 2 页装备数据
    python main.py --type role  --area 1            # 抓取大区 1 的角色数据
    python main.py --type pet   --server 1001       # 抓取指定服务器宠物数据
    python main.py --type yuanshen                  # 抓取全服元神数据

注意: robots.txt 声明 Disallow: /，仅限个人学习使用，请控制请求频率。
"""

import argparse
import logging
import sys
from typing import Optional

import config
from auth import ensure_session, login_and_save_cookies
from storage import init_db

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%H:%M:%S",
)
logger = logging.getLogger("main")


def cmd_login(args: argparse.Namespace) -> None:
    login_and_save_cookies(args.cookie_file)


def cmd_scrape(args: argparse.Namespace) -> None:
    init_db(args.db)

    try:
        session = ensure_session(args.cookie_file, auto_relogin=False)
    except (FileNotFoundError, RuntimeError) as exc:
        logger.error("%s", exc)
        sys.exit(1)

    scrape_type: str = args.type.lower()
    page_limit: int = args.page_limit
    area_id: Optional[str] = args.area
    server_id: Optional[str] = args.server

    total = 0

    if scrape_type == "equip":
        from scrapers.equip import scrape_equips
        for page_items in scrape_equips(
            session,
            area_id=area_id,
            server_id=server_id,
            equip_type=args.equip_type,
            page_size=config.DEFAULT_PAGE_SIZE,
            max_pages=page_limit,
        ):
            total += len(page_items)
            logger.info("Cumulative equips scraped: %d", total)

    elif scrape_type == "role":
        from scrapers.role import scrape_roles
        for page_items in scrape_roles(
            session,
            area_id=area_id,
            server_id=server_id,
            school=args.school,
            min_level=args.min_level,
            max_level=args.max_level,
            page_size=config.DEFAULT_PAGE_SIZE,
            max_pages=page_limit,
        ):
            total += len(page_items)
            logger.info("Cumulative roles scraped: %d", total)

    elif scrape_type == "pet":
        from scrapers.pet import scrape_pets
        for page_items in scrape_pets(
            session,
            area_id=area_id,
            server_id=server_id,
            page_size=config.DEFAULT_PAGE_SIZE,
            max_pages=page_limit,
        ):
            total += len(page_items)
            logger.info("Cumulative pets scraped: %d", total)

    elif scrape_type == "yuanshen":
        from scrapers.yuanshen import scrape_yuanshen
        for page_items in scrape_yuanshen(
            session,
            area_id=area_id,
            server_id=server_id,
            page_size=config.DEFAULT_PAGE_SIZE,
            max_pages=page_limit,
        ):
            total += len(page_items)
            logger.info("Cumulative yuanshen scraped: %d", total)

    else:
        logger.error("Unknown type '%s'. Choose from: equip, role, pet, yuanshen", scrape_type)
        sys.exit(1)

    logger.info("Scraping complete. Total records: %d. DB: %s", total, args.db)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="梦幻西游藏宝阁爬虫",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "--cookie-file",
        default=config.COOKIE_FILE,
        metavar="FILE",
        help=f"Cookie 存储文件 (default: {config.COOKIE_FILE})",
    )
    parser.add_argument(
        "--db",
        default=config.DB_FILE,
        metavar="FILE",
        help=f"SQLite 数据库文件 (default: {config.DB_FILE})",
    )

    # ---- Mode flags ----
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument(
        "--login",
        action="store_true",
        help="打开浏览器手动登录，刷新 Cookie",
    )
    mode.add_argument(
        "--type",
        choices=["equip", "role", "pet", "yuanshen"],
        metavar="TYPE",
        help="抓取类型: equip | role | pet | yuanshen",
    )

    # ---- Common scrape options ----
    parser.add_argument("--area", metavar="AREA_ID", help="大区 ID（可选）")
    parser.add_argument("--server", metavar="SERVER_ID", help="服务器 ID（可选）")
    parser.add_argument(
        "--page-limit",
        type=int,
        default=config.DEFAULT_MAX_PAGES,
        metavar="N",
        help=f"最多抓取页数 (default: {config.DEFAULT_MAX_PAGES})",
    )

    # ---- Type-specific options ----
    parser.add_argument("--equip-type", metavar="CODE", help="装备类型代码 (equip only)")
    parser.add_argument("--school", metavar="SCHOOL", help="门派名称/代码 (role only)")
    parser.add_argument("--min-level", type=int, metavar="N", help="最低等级 (role only)")
    parser.add_argument("--max-level", type=int, metavar="N", help="最高等级 (role only)")

    return parser


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()

    if args.login:
        cmd_login(args)
    else:
        cmd_scrape(args)


if __name__ == "__main__":
    main()
