"""SQLite storage layer for cbg_scraper."""

import sqlite3
import logging
from contextlib import contextmanager
from typing import List

from config import DB_FILE
from models import Equip, Role, Pet, Yuanshen

logger = logging.getLogger(__name__)


@contextmanager
def _get_conn(db_file: str):
    conn = sqlite3.connect(db_file)
    conn.row_factory = sqlite3.Row
    try:
        yield conn
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()


def init_db(db_file: str = DB_FILE) -> None:
    """Create all tables if they do not exist."""
    with _get_conn(db_file) as conn:
        conn.executescript("""
            CREATE TABLE IF NOT EXISTS equips (
                id              TEXT PRIMARY KEY,
                equip_name      TEXT,
                equip_level     INTEGER,
                price           INTEGER,
                init_damage     INTEGER,
                init_defense    INTEGER,
                init_hp         INTEGER,
                init_dex        INTEGER,
                gem_level       INTEGER,
                special_effect  TEXT,
                equip_type      TEXT,
                area_name       TEXT,
                server_name     TEXT,
                seller_roleid   TEXT,
                is_locked       INTEGER DEFAULT 0,
                scraped_at      DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS roles (
                id              TEXT PRIMARY KEY,
                role_name       TEXT,
                role_level      INTEGER,
                school          TEXT,
                price           INTEGER,
                area_name       TEXT,
                server_name     TEXT,
                seller_roleid   TEXT,
                extra_info      TEXT,
                scraped_at      DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS pets (
                id              TEXT PRIMARY KEY,
                pet_name        TEXT,
                pet_type        TEXT,
                level           INTEGER,
                quality         TEXT,
                skills          TEXT,
                evolve_skills   TEXT,
                price           INTEGER,
                area_name       TEXT,
                server_name     TEXT,
                seller_roleid   TEXT,
                scraped_at      DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS yuanshen (
                id              TEXT PRIMARY KEY,
                yuanshen_name   TEXT,
                level           INTEGER,
                quality         TEXT,
                attributes      TEXT,
                price           INTEGER,
                area_name       TEXT,
                server_name     TEXT,
                seller_roleid   TEXT,
                scraped_at      DATETIME DEFAULT CURRENT_TIMESTAMP
            );
        """)
    logger.info("Database initialised: %s", db_file)


# ---------------------------------------------------------------------------
# Upsert helpers (INSERT OR REPLACE)
# ---------------------------------------------------------------------------

def save_equips(equips: List[Equip], db_file: str = DB_FILE) -> int:
    if not equips:
        return 0
    sql = """
        INSERT OR REPLACE INTO equips
            (id, equip_name, equip_level, price, init_damage, init_defense,
             init_hp, init_dex, gem_level, special_effect, equip_type,
             area_name, server_name, seller_roleid, is_locked, scraped_at)
        VALUES
            (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
    """
    rows = [
        (
            e.id, e.equip_name, e.equip_level, e.price,
            e.init_damage, e.init_defense, e.init_hp, e.init_dex,
            e.gem_level, e.special_effect, e.equip_type,
            e.area_name, e.server_name, e.seller_roleid,
            int(e.is_locked),
            e.scraped_at.isoformat() if e.scraped_at else None,
        )
        for e in equips
    ]
    with _get_conn(db_file) as conn:
        conn.executemany(sql, rows)
    logger.debug("Saved %d equip records.", len(rows))
    return len(rows)


def save_roles(roles: List[Role], db_file: str = DB_FILE) -> int:
    if not roles:
        return 0
    sql = """
        INSERT OR REPLACE INTO roles
            (id, role_name, role_level, school, price,
             area_name, server_name, seller_roleid, extra_info, scraped_at)
        VALUES (?,?,?,?,?,?,?,?,?,?)
    """
    rows = [
        (
            r.id, r.role_name, r.role_level, r.school, r.price,
            r.area_name, r.server_name, r.seller_roleid, r.extra_info,
            r.scraped_at.isoformat() if r.scraped_at else None,
        )
        for r in roles
    ]
    with _get_conn(db_file) as conn:
        conn.executemany(sql, rows)
    logger.debug("Saved %d role records.", len(rows))
    return len(rows)


def save_pets(pets: List[Pet], db_file: str = DB_FILE) -> int:
    if not pets:
        return 0
    sql = """
        INSERT OR REPLACE INTO pets
            (id, pet_name, pet_type, level, quality, skills,
             evolve_skills, price, area_name, server_name, seller_roleid, scraped_at)
        VALUES (?,?,?,?,?,?,?,?,?,?,?,?)
    """
    rows = [
        (
            p.id, p.pet_name, p.pet_type, p.level, p.quality,
            p.skills, p.evolve_skills, p.price,
            p.area_name, p.server_name, p.seller_roleid,
            p.scraped_at.isoformat() if p.scraped_at else None,
        )
        for p in pets
    ]
    with _get_conn(db_file) as conn:
        conn.executemany(sql, rows)
    logger.debug("Saved %d pet records.", len(rows))
    return len(rows)


def save_yuanshen(items: List[Yuanshen], db_file: str = DB_FILE) -> int:
    if not items:
        return 0
    sql = """
        INSERT OR REPLACE INTO yuanshen
            (id, yuanshen_name, level, quality, attributes, price,
             area_name, server_name, seller_roleid, scraped_at)
        VALUES (?,?,?,?,?,?,?,?,?,?)
    """
    rows = [
        (
            y.id, y.yuanshen_name, y.level, y.quality, y.attributes, y.price,
            y.area_name, y.server_name, y.seller_roleid,
            y.scraped_at.isoformat() if y.scraped_at else None,
        )
        for y in items
    ]
    with _get_conn(db_file) as conn:
        conn.executemany(sql, rows)
    logger.debug("Saved %d yuanshen records.", len(rows))
    return len(rows)
