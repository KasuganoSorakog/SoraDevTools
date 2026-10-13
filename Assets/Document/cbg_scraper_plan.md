# 梦幻西游藏宝阁 Python 爬虫 - 实施计划

## Context

梦幻西游电脑版藏宝阁（xyq.cbg.163.com）是网易运营的游戏内物品交易平台。用户希望抓取其中的装备、角色、宠物、元神等商品数据，用于行情分析。用户拥有藏宝阁账号（可处理登录验证），技术栈选择 Python。

**注意事项**：该网站 `robots.txt` 声明 `Disallow: /`（禁止全站爬取）。建议仅限个人使用，控制请求频率，避免影响平台正常运营。

---

## 网站技术分析

### API 架构

- 基础 URL：`https://xyq.cbg.163.com/cgi-bin/`
- 接口风格：CGI Python 脚本，参数以 QueryString 形式传递
- 响应格式：JSON（部分为 HTML 模板渲染）
- 认证方式：Cookie-based Session（未登录返回 HTTP 303 重定向至登录页）

### 已确认的核心接口

| 接口 | 说明 |
|------|------|
| `/cgi-bin/xyq_overall_search.py?act=show_search_role_form` | 角色搜索（含过滤参数） |
| `/cgi-bin/equipquery.py?act=show_overall_search_equip` | 装备搜索列表 |
| `/cgi-bin/suggest.py` | 搜索关键词补全 |
| `/cgi-bin/recommend.py` | 推荐商品 |
| `/cgi-bin/query.py?act=recommend_search` | 推荐搜索（宠物/元神等） |

### 可抓取的数据类型

- **装备**：名称、等级、属性（攻击/防御/HP/速度）、宝石、特效、价格、服务器、卖家
- **角色**：等级、门派、角色属性、宠物配置、价格
- **宠物**：属性、技能、进化技能
- **元神**：相关属性数据
- **通用**：成交流水号、卖家 ID、上架时间、是否锁定

### 防爬机制（已识别）

1. **Session 认证**：所有数据接口需要有效登录 Cookie，否则 HTTP 303 跳转
2. **check_sign_code**：部分接口有签名校验字段（初步判断为服务端参数验证，未见前端加密）
3. **行为追踪**：sigma-other-cbg.proxima.nie.netease.com 上报用户行为
4. **频率限制**：可能存在 IP 级别限流（待实测）

---

## 项目结构

独立 Python 项目，不集成到 SoraDevTools Unity 工程中。

```
cbg_scraper/
├── main.py               # 入口：控制抓取流程
├── auth.py               # 登录模块（获取并维护 Cookie）
├── config.py             # 配置（延迟、页大小、目标服务器等）
├── models.py             # 数据模型定义
├── storage.py            # SQLite 存储层
├── requirements.txt
└── scrapers/
    ├── __init__.py
    ├── _base.py          # 共用工具（请求、解析、延迟）
    ├── equip.py          # 装备数据抓取
    ├── role.py           # 角色数据抓取
    ├── pet.py            # 宠物数据抓取
    └── yuanshen.py       # 元神数据抓取
```

---

## 实施步骤

### Step 1：环境准备

**依赖库**（`requirements.txt`）：

```
requests>=2.31.0
playwright>=1.40.0       # 处理登录流程（支持 JS、可手动过验证码）
beautifulsoup4>=4.12.0   # HTML 解析（部分接口返回 HTML）
lxml>=4.9.0
sqlite3                  # 标准库
```

安装 Playwright 浏览器：

```bash
pip install -r requirements.txt
playwright install chromium
```

---

### Step 2：认证模块（auth.py）

**方案**：使用 Playwright 打开真实浏览器，让用户手动完成登录（包括可能的滑块验证码），然后提取 Cookie 保存到本地文件，后续用 requests 携带 Cookie 访问接口。

**核心函数**：

```python
def login_and_save_cookies(cookie_file="cookies.json"):
    # 1. Playwright 打开 https://cbg.163.com 登录页
    # 2. 等待用户手动登录（timeout=180s）
    # 3. 检测登录成功（检查特定 Cookie 存在）
    # 4. 导出 cookies 到 JSON 文件

def load_session(cookie_file="cookies.json") -> requests.Session:
    # 1. 读取 cookies.json
    # 2. 创建 requests.Session 并注入 Cookie
    # 3. 设置 User-Agent 和 Referer Header
    # 4. 验证 Session 有效性（请求 recommend.py，检测 HTTP 303）

def ensure_session(cookie_file, auto_relogin=False) -> Session:
    # 封装加载 + 验证，支持自动重登录
```

---

### Step 3：装备抓取器（scrapers/equip.py）

**接口**：`/cgi-bin/equipquery.py?act=show_overall_search_equip`

**查询参数**：

| 参数 | 说明 |
|------|------|
| `area_id` / `server_id` | 服务器 ID |
| `equip_type` | 装备类型（枚举值待确认） |
| `page` / `pagesize` | 分页（默认每页 20 条） |
| `price_low` / `price_high` | 价格范围（单位：分） |
| `client_type=web` | 固定参数 |

**解析策略**：

- 优先尝试 JSON 解析（`_parse_equip_json`）
- 失败时降级为 BeautifulSoup HTML 解析（`_parse_equip_html`）
- 每页请求后随机 sleep 1.5–3.0 秒

---

### Step 4：角色/宠物/元神抓取器

**角色接口**：`/cgi-bin/xyq_overall_search.py?act=show_search_role_form`

| 参数 | 说明 |
|------|------|
| `min_level` / `max_level` | 等级范围 |
| `school` | 门派筛选 |
| `sort_by` | 排序方式 |

**宠物/元神接口**：`/cgi-bin/query.py?act=recommend_search`

通过 `recommend_type` 参数切换类型（`pet` / `yuanshen`）。

---

### Step 5：存储层（storage.py）

SQLite 数据库，4 张表：

```sql
CREATE TABLE equips (
    id              TEXT PRIMARY KEY,    -- game_ordersn
    equip_name      TEXT,
    equip_level     INTEGER,
    price           INTEGER,             -- 单位：分
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

CREATE TABLE roles (
    id TEXT PRIMARY KEY, role_name TEXT, role_level INTEGER,
    school TEXT, price INTEGER, area_name TEXT, server_name TEXT,
    seller_roleid TEXT, extra_info TEXT, scraped_at DATETIME
);

CREATE TABLE pets (
    id TEXT PRIMARY KEY, pet_name TEXT, pet_type TEXT, level INTEGER,
    quality TEXT, skills TEXT, evolve_skills TEXT, price INTEGER,
    area_name TEXT, server_name TEXT, seller_roleid TEXT, scraped_at DATETIME
);

CREATE TABLE yuanshen (
    id TEXT PRIMARY KEY, yuanshen_name TEXT, level INTEGER, quality TEXT,
    attributes TEXT, price INTEGER, area_name TEXT, server_name TEXT,
    seller_roleid TEXT, scraped_at DATETIME
);
```

所有写入使用 `INSERT OR REPLACE`（upsert 语义），支持重复抓取去重。

---

### Step 6：主控流程（main.py）

```bash
# 重新登录刷新 Cookie
python main.py --login

# 抓取全服装备（默认最多 50 页）
python main.py --type equip

# 抓取指定大区角色
python main.py --type role --area 1

# 限制页数（测试用）
python main.py --type equip --page-limit 2

# 指定服务器宠物
python main.py --type pet --server 1001
```

---

## 关键风险与对策

| 风险 | 对策 |
|------|------|
| Session 过期 | 检测 HTTP 303 响应，提示用户运行 `--login` |
| IP 限流 | 请求间隔 1.5–3s 随机延迟，控制单次任务页数 |
| 登录验证码 | Playwright 手动登录模式，用户自行完成验证（timeout=180s） |
| 响应格式变化 | 解析失败时记录原始文本，双解析路径（JSON → HTML 降级） |
| check_sign_code 验证 | 携带完整浏览器 Cookie 后大概率通过，如失败需分析签名算法 |
| 未知响应字段 | role/yuanshen 额外字段序列化为 JSON 存入 extra_info 列 |

---

## 验证方式

```bash
# Step 1：登录
python main.py --login

# Step 2：测试抓取
python main.py --type equip --page-limit 2

# Step 3：验证数据库
sqlite3 cbg_data.db "SELECT id, equip_name, price, server_name FROM equips LIMIT 5;"

# Step 4：检查字段完整性
sqlite3 cbg_data.db "SELECT COUNT(*) FROM equips WHERE price > 0;"
```

---

## 待确认事项（首次运行后验证）

- [ ] 登录入口 URL（`cbg.163.com` 还是 `xyq.cbg.163.com`）
- [ ] 服务器/大区列表的获取接口
- [ ] 装备类型枚举值（`equip_type` 参数的合法值）
- [ ] 响应是纯 JSON 还是嵌入 HTML 的数据
- [ ] `check_sign_code` 在携带正确 Cookie 后是否自动通过
- [ ] 宠物/元神的 `recommend_type` 实际参数值

---

## 实现日期

2026-02-21
