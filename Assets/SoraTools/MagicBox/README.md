# MagicBox - 本地开发资源库管理工具

MagicBox 是一款专为 Unity 开发者设计的本地资源管理工具，提供统一的资源存储、检索与组织中心。支持导入各类开发资产（Unity 包、脚本、贴图、模型等），并通过 SQLite 数据库实现高效的全文搜索、标签管理与文件夹分类。

---

## 功能概览

### 多库管理
- 创建多个独立的资源库，每个库拥有独立的 SQLite 数据库
- 启动时自动打开上次使用的库
- 支持云同步（兼容 Syncthing `.stignore` 规则）
- 库锁文件防止多实例冲突

### 资源导入
- 同步/异步批量导入 API
- 支持从 Windows 资源管理器拖拽导入
- 自动识别 15 种资源类型
- MD5 哈希去重，防止重复导入
- 大文件（>100MB）引用模式，不拷贝文件只记录路径
- 图片自动生成缩略图

### 支持的资源类型（15 种）

| 类型 | 说明 |
|------|------|
| UnityPackage | Unity 资源包（.unitypackage） |
| ZipArchive | 压缩包（.zip / .7z / .rar） |
| CSharpScript | C# 脚本 |
| ShaderFile | Shader 着色器文件 |
| Prefab | Unity 预制体 |
| Material | 材质 |
| Texture | 贴图 / 图片 |
| Model3D | 3D 模型 |
| Audio | 音频文件 |
| Animation | 动画文件 |
| Font | 字体 |
| Document | 文档（PDF / Word 等） |
| Executable | 可执行文件 |
| Folder | 文件夹（整目录导入） |
| Other | 其他 |

### 搜索与筛选
- FTS5 全文搜索（名称、文件名、描述）
- 多条件过滤：类型、标签、日期范围、评分
- 排序方式：名称、导入时间、修改时间、大小、评分
- FTS5 不可用时自动降级为 LIKE 搜索

### 层级文件夹
- 无限层级嵌套，支持拖拽排序
- 文件夹颜色标签，视觉区分
- 智能文件夹（基于过滤条件自动聚合资源）

### 标签系统
- 每个资源支持多标签
- 标签分组与颜色自定义
- 按文件类型自动打标签
- 标签使用次数统计

### 资源元数据
- 名称、描述、版本、来源 URL
- Unity 版本兼容范围（最低/最高版本）
- 1-5 星评分
- 文件哈希、大小、创建/修改日期
- 自定义缩略图
- 引用模式标识

### UI 界面
- Grid 卡片视图 / List 列表视图切换
- 侧边栏：文件夹树 + 标签面板
- 详情面板：完整资源信息与编辑能力
- 实时搜索即时刷新
- 拖拽到文件夹的视觉反馈

---

## 架构设计

MagicBox 采用三层架构：

```
UI 层（UI Toolkit：UXML / USS）
        ↓
Service 层（LibraryManager、ImportService、SearchService、TagService、FolderService）
        ↓
Data 层（LibraryDatabase → SQLiteConnection）
```

### 数据库结构（SQLite）

| 表名 | 说明 |
|------|------|
| `resources` | 资源核心记录，外键关联 folders |
| `folders` | 层级文件夹树（parent_id 自关联） |
| `tags` | 标签定义（含分组与颜色） |
| `resource_tags` | 资源与标签的多对多关联表 |

索引建立在：`resource_type`、`folder_id`、`rating`、`imported_at`。
全文搜索使用 FTS5 虚拟表，覆盖字段：`rid`、`name`、`file_name`、`description`。

### 主要文件说明

**核心层**

| 文件 | 说明 |
|------|------|
| `LibraryDatabase.cs` | SQLite CRUD 操作与 Schema 管理 |
| `AppSettings.cs` | 用户偏好设置（库路径、视图模式、语言等） |
| `SQLiteConnection.cs` | 底层 SQLite 封装 |
| `DatabaseMigrator.cs` | 数据库版本管理与迁移 |
| `Loc.cs` | 国际化 / 多语言支持 |

**服务层**

| 文件 | 说明 |
|------|------|
| `LibraryManager.cs` | 库生命周期管理（创建/打开/关闭）、锁文件 |
| `ImportService.cs` | 文件导入、哈希计算、目录遍历 |
| `SearchService.cs` | 查询构建与执行 |
| `TagService.cs` | 标签 CRUD 与批量操作 |
| `FolderService.cs` | 文件夹树操作 |
| `ServiceContainer.cs` | 服务依赖注入容器 |

**UI 层**

| 文件 | 说明 |
|------|------|
| `MagicBoxApp.cs` | 主 MonoBehaviour，服务初始化与事件协调 |
| `ContentViewModel.cs` | 展示状态与数据绑定 |
| `WelcomeView.cs` | 无库打开时的初始界面 |
| `ToolbarView.cs` | 搜索、过滤、排序、视图模式控件 |
| `SidebarView.cs` | 文件夹树 + 标签面板 |
| `ContentGridView.cs` | 卡片式资源网格视图 |
| `ContentListView.cs` | 表格式资源列表视图 |
| `DetailPanelView.cs` | 资源详情与编辑面板 |

---

## 配置参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| 引用模式阈值 | 100MB | 大于此值的文件不拷贝，仅记录路径 |
| 缩略图尺寸 | 130px | 自动生成缩略图的边长 |
| 界面语言 | 中文/英文 | 支持运行时切换 |
| 最近库记录数 | 10 | 保留最近打开的库数量 |
| 视图模式 | Grid/List | 用户可切换 |

---

## 存储路径

| 内容 | 路径 |
|------|------|
| 用户设置 | `%APPDATA%/MagicBox/settings.json` |
| 数据库 | `{库根目录}/magicbox.db` |
| 文件存储 | `{库根目录}/files/` |
| 缩略图 | `{库根目录}/thumbnails/` |

---

## 依赖

- Unity 2022.3.56f1+
- SQLite（内嵌，无需额外安装）
- .NET Standard 2.1 / C# 9.0
- Windows 平台（Win32 文件对话框与拖拽集成）
