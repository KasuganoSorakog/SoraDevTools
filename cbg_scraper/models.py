"""Data models for cbg_scraper."""

from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional


@dataclass
class Equip:
    """梦幻西游藏宝阁装备数据模型"""
    id: str                            # game_ordersn, primary key
    equip_name: str = ""
    equip_level: int = 0
    price: int = 0                     # 单位：分
    init_damage: int = 0               # 初始攻击
    init_defense: int = 0              # 初始防御
    init_hp: int = 0                   # 初始气血
    init_dex: int = 0                  # 初始速度
    gem_level: int = 0                 # 宝石等级
    special_effect: str = ""           # 特效描述
    equip_type: str = ""               # 装备类型
    area_name: str = ""                # 大区名称
    server_name: str = ""              # 服务器名称
    seller_roleid: str = ""            # 卖家角色 ID
    is_locked: bool = False            # 是否锁定
    scraped_at: Optional[datetime] = None

    def __post_init__(self):
        if self.scraped_at is None:
            self.scraped_at = datetime.now()


@dataclass
class Role:
    """梦幻西游藏宝阁角色数据模型"""
    id: str                            # game_ordersn
    role_name: str = ""
    role_level: int = 0
    school: str = ""                   # 门派
    price: int = 0
    area_name: str = ""
    server_name: str = ""
    seller_roleid: str = ""
    extra_info: str = ""               # JSON 存储其余属性
    scraped_at: Optional[datetime] = None

    def __post_init__(self):
        if self.scraped_at is None:
            self.scraped_at = datetime.now()


@dataclass
class Pet:
    """梦幻西游藏宝阁宠物数据模型"""
    id: str
    pet_name: str = ""
    pet_type: str = ""                 # 宠物种类
    level: int = 0
    quality: str = ""                  # 品质
    skills: str = ""                   # 技能列表（逗号分隔或 JSON）
    evolve_skills: str = ""            # 进化技能
    price: int = 0
    area_name: str = ""
    server_name: str = ""
    seller_roleid: str = ""
    scraped_at: Optional[datetime] = None

    def __post_init__(self):
        if self.scraped_at is None:
            self.scraped_at = datetime.now()


@dataclass
class Yuanshen:
    """梦幻西游藏宝阁元神数据模型"""
    id: str
    yuanshen_name: str = ""
    level: int = 0
    quality: str = ""
    attributes: str = ""               # 属性（JSON 字符串）
    price: int = 0
    area_name: str = ""
    server_name: str = ""
    seller_roleid: str = ""
    scraped_at: Optional[datetime] = None

    def __post_init__(self):
        if self.scraped_at is None:
            self.scraped_at = datetime.now()
