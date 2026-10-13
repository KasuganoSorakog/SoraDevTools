# MagicBox 开发资源管理器 - 开发策划书

## 一、项目概述

### 1.1 项目名称
**MagicBox Resource Manager** (MagicBox 资源管理器)

### 1.2 定位
面向 Unity 开发者的本地开发资源管理工具，以 Unity Editor 扩展窗口为载体，
参考 Eagle 的设计理念，实现对 .unitypackage、脚本、压缩包、工具等开发资源的
**收集、整理、检索、快速导入** 一站式管理。

### 1.3 核心痛点
- 开发资源散落在各个磁盘/文件夹中，查找困难
- 无法快速知道某个 package 的用途、版本、适用场景
- 跨项目复用资源时需要手动翻找、复制
- 缺少标签、分类、搜索等结构化管理手段

### 1.4 目标用户
Unity 开发者（个人/小团队），暂不涉及用户管理系统

---

## 二、功能模块设计

### 2.1 资源库核心 (Library Core)

| 功能 | 说明 |
|------|------|
| **资源库创建** | 支持创建多个资源库（如"插件库"、"美术资源库"、"工具库"），每个库对应一个独立的存储目录和元数据库 |
| **资源导入** | 将文件/文件夹导入资源库，自动复制到库目录并生成元数据记录 |
| **资源引用模式** | 对于大文件(>100MB)，支持"引用模式"——不复制文件，仅记录原始路径 |
| **库路径配置** | 资源库可以放在任意磁盘（含网络盘、NAS），支持跨机器访问 |

### 2.2 分类与文件夹系统 (Category System)

| 功能 | 说明 |
|------|------|
| **文件夹树** | 支持多层级文件夹结构（左侧面板树形展示），类似 Eagle 的文件夹面板 |
| **拖拽排序** | 文件夹支持拖拽排序和嵌套 |
| **文件夹图标/颜色** | 每个文件夹可设置颜色标记，便于视觉区分 |
| **智能文件夹** | 基于条件自动收集资源（如：所有 .unitypackage、所有标签含"UI"的资源） |

### 2.3 标签系统 (Tagging System)

| 功能 | 说明 |
|------|------|
| **标签管理** | 创建、编辑、删除、合并标签 |
| **多标签** | 每个资源可关联多个标签 |
| **标签分组** | 标签可按组管理（如"类型"组: UI/Shader/插件；"引擎版本"组: 2021/2022/2023） |
| **快捷标签** | 导入时自动根据文件类型添加标签（如 .unitypackage → "UnityPackage"） |
| **标签颜色** | 标签支持颜色区分 |

### 2.4 搜索与筛选 (Search & Filter)

| 功能 | 说明 |
|------|------|
| **全局搜索** | 按名称、标签、描述进行关键词搜索 |
| **类型筛选** | 按文件类型快速过滤（UnityPackage / ZIP / Script / Prefab / 其他） |
| **组合筛选** | 多条件组合：类型 + 标签 + 日期范围 + 文件大小 |
| **排序** | 按名称 / 导入日期 / 修改日期 / 文件大小 / 评分 排序 |
| **模糊匹配** | 搜索支持拼音首字母和模糊匹配 |

### 2.5 资源详情与元数据 (Metadata)

| 功能 | 说明 |
|------|------|
| **基础信息** | 文件名、类型、大小、创建/修改日期、存储路径 |
| **自定义备注** | 为资源添加文字说明（用途、注意事项、版本信息等） |
| **星级评分** | 1-5 星评分，用于标记重要度/质量 |
| **缩略图/预览** | 自动根据类型生成预览图标；支持手动上传缩略图 |
| **版本记录** | 记录资源版本号（如 DOTween Pro v1.0.335） |
| **兼容性标注** | 标注适用的 Unity 版本范围 |
| **来源链接** | 记录资源的来源 URL（Asset Store、GitHub 等） |

### 2.6 快速操作 (Quick Actions)

| 功能 | 说明 |
|------|------|
| **导入到当前项目** | 一键将 .unitypackage 导入当前 Unity 项目 |
| **复制到指定路径** | 将资源复制到自定义目录 |
| **打开所在文件夹** | 在文件资源管理器中打开资源位置 |
| **打开文件** | 直接打开/执行文件 |
| **批量操作** | 批量添加标签、移动分类、删除 |

### 2.7 资源导入方式 (Import Methods)

| 方式 | 说明 |
|------|------|
| **拖拽导入** | 从文件管理器拖拽文件到窗口 |
| **文件夹扫描** | 指定文件夹路径，批量扫描并导入 |
| **手动添加** | 通过文件选择对话框添加 |
| **监视文件夹** | 监视指定文件夹，新增文件自动导入（可选功能，后期实现） |

---

## 三、界面设计 (UI Layout)

### 3.1 整体布局（参考 Eagle 风格）

```
┌─────────────────────────────────────────────────────────────────┐
│  [资源库选择下拉] │ 🔍 搜索框          │ [筛选] [排序] [视图切换] │
├──────────────────┼──────────────────────────────────────────────┤
│                  │                                              │
│  📁 全部资源      │  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐       │
│  📁 未分类        │  │ 图标  │ │ 图标  │ │ 图标  │ │ 图标  │       │
│                  │  │      │ │      │ │      │ │      │       │
│  ▼ 📁 插件       │  │ 名称  │ │ 名称  │ │ 名称  │ │ 名称  │       │
│    📁 UI 插件    │  │ 标签  │ │ 标签  │ │ 标签  │ │ 标签  │       │
│    📁 工具插件    │  └──────┘ └──────┘ └──────┘ └──────┘       │
│    📁 动画插件    │                                              │
│                  │  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐       │
│  ▼ 📁 脚本       │  │ 图标  │ │ 图标  │ │ 图标  │ │ 图标  │       │
│    📁 编辑器扩展  │  │      │ │      │ │      │ │      │       │
│    📁 工具脚本    │  │ 名称  │ │ 名称  │ │ 名称  │ │ 名称  │       │
│                  │  │ 标签  │ │ 标签  │ │ 标签  │ │ 标签  │       │
│  ▼ 📁 美术资源    │  └──────┘ └──────┘ └──────┘ └──────┘       │
│    📁 字体       │                                              │
│    📁 模型       │──────────────────────────────────────────────│
│                  │  ► 资源详情面板（点击资源后展开）              │
│  ─────────────── │  │ 名称: DOTween Pro                         │
│  🏷️ 标签面板     │  │ 版本: v1.0.335  ⭐⭐⭐⭐⭐                │
│  • UnityPackage  │  │ 类型: UnityPackage  大小: 570 KB          │
│  • 插件          │  │ 标签: [动画] [DOTween] [插件]              │
│  • UI            │  │ 备注: 强大的动画插件，支持...              │
│  • 工具          │  │ [导入到项目] [打开文件夹] [编辑信息]       │
│  • 动画          │                                              │
└──────────────────┴──────────────────────────────────────────────┘
```

### 3.2 视图模式

| 模式 | 说明 |
|------|------|
| **网格视图** | 卡片式排列，显示缩略图 + 名称 + 标签（默认模式） |
| **列表视图** | 表格式排列，显示名称、类型、大小、日期、标签等列 |
| **详情视图** | 左侧列表 + 右侧详情面板 |

### 3.3 颜色/主题
- 默认深色主题（与 Unity Editor 风格统一）
- 使用 Unity 内置 EditorStyles 保持一致性

---

## 四、技术方案

### 4.1 技术架构

```
┌──────────────────────────────────────────────────┐
│              Unity Editor Extension               │
│           (EditorWindow + UI Toolkit)             │
├──────────────────────────────────────────────────┤
│                                                  │
│  ┌──────────────┐  ┌──────────────┐             │
│  │  UI Layer     │  │  UI Layer     │             │
│  │  (UXML/USS)  │  │  (IMGUI备选)  │             │
│  └──────┬───────┘  └──────┬───────┘             │
│         │                  │                     │
│  ┌──────▼──────────────────▼───────┐             │
│  │        ViewModel Layer          │             │
│  │  (数据绑定、状态管理、命令)       │             │
│  └──────────────┬──────────────────┘             │
│                 │                                │
│  ┌──────────────▼──────────────────┐             │
│  │        Service Layer            │             │
│  │  ┌────────┐ ┌────────┐ ┌─────┐ │             │
│  │  │Library │ │Search  │ │Tag  │ │             │
│  │  │Service │ │Service │ │Svc  │ │             │
│  │  └────────┘ └────────┘ └─────┘ │             │
│  └──────────────┬──────────────────┘             │
│                 │                                │
│  ┌──────────────▼──────────────────┐             │
│  │        Data Layer               │             │
│  │  ┌──────────┐ ┌──────────────┐  │             │
│  │  │ SQLite   │ │ FileSystem   │  │             │
│  │  │ Database │ │ Operations   │  │             │
│  │  └──────────┘ └──────────────┘  │             │
│  └─────────────────────────────────┘             │
└──────────────────────────────────────────────────┘
```

### 4.2 核心技术选型

| 层面 | 技术 | 理由 |
|------|------|------|
| **载体** | Unity Editor Extension (EditorWindow) | 直接集成到 Unity 工作流，无需额外启动应用；可打包为 .unitypackage 在项目间复用 |
| **UI 框架** | UI Toolkit (UXML + USS) | Unity 官方推荐的现代 UI 方案，支持 Flex 布局、样式表、数据绑定，适合复杂界面 |
| **UI 备选** | IMGUI (OnGUI) | 部分简单控件用 IMGUI 实现，降低复杂度 |
| **数据库** | SQLite (通过 Mono.Data.Sqlite) | 轻量级嵌入式数据库，无需安装，单文件存储，查询性能好 |
| **数据序列化** | JSON (JsonUtility / Newtonsoft.Json) | 配置文件、导出/导入用 JSON 格式 |
| **缩略图** | Unity AssetPreview + 自定义渲染 | 利用 Unity 内置的资源预览能力 |
| **文件操作** | System.IO + Unity AssetDatabase | 文件复制/移动用 System.IO，Unity 资源操作用 AssetDatabase |
| **搜索算法** | SQLite FTS5 (全文搜索) + 内存缓存 | 高效的全文搜索能力 |

### 4.3 数据结构设计

#### 资源库 (Library)
```csharp
public class ResourceLibrary
{
    public string Id;            // GUID
    public string Name;          // 库名称
    public string RootPath;      // 库根目录路径
    public string DatabasePath;  // SQLite 数据库文件路径
    public DateTime CreatedAt;
    public DateTime UpdatedAt;
}
```

#### 资源条目 (ResourceItem)
```csharp
public class ResourceItem
{
    public string Id;               // GUID
    public string Name;             // 显示名称
    public string FileName;         // 原始文件名
    public string RelativePath;     // 在库中的相对路径
    public string OriginalPath;     // 原始路径（引用模式）
    public ResourceType Type;       // 资源类型枚举
    public long FileSize;           // 文件大小(bytes)
    public string FileHash;         // 文件 MD5 哈希（去重用）
    public int Rating;              // 评分 0-5
    public string Version;          // 版本号
    public string Description;      // 备注说明
    public string SourceUrl;        // 来源链接
    public string UnityVersionMin;  // 最低 Unity 版本
    public string UnityVersionMax;  // 最高 Unity 版本
    public string ThumbnailPath;    // 缩略图路径
    public bool IsReference;        // 是否为引用模式
    public DateTime ImportedAt;     // 导入时间
    public DateTime FileModifiedAt; // 文件修改时间
    public List<string> TagIds;     // 关联标签 ID 列表
    public string FolderId;         // 所属文件夹 ID
}
```

#### 资源类型枚举
```csharp
public enum ResourceType
{
    UnityPackage,   // .unitypackage
    ZipArchive,     // .zip, .rar, .7z
    CSharpScript,   // .cs
    ShaderFile,     // .shader, .cginc, .hlsl
    Prefab,         // .prefab
    Material,       // .mat
    Texture,        // .png, .jpg, .psd, .tga
    Model,          // .fbx, .obj
    Audio,          // .wav, .mp3, .ogg
    Animation,      // .anim, .controller
    Font,           // .ttf, .otf
    Document,       // .pdf, .md, .txt
    Executable,     // .exe
    Folder,         // 文件夹
    Other           // 其他
}
```

#### 文件夹 (Folder)
```csharp
public class ResourceFolder
{
    public string Id;           // GUID
    public string Name;         // 文件夹名称
    public string ParentId;     // 父文件夹 ID (null=根)
    public int SortOrder;       // 排序序号
    public string ColorTag;     // 颜色标记 (#hex)
    public bool IsSmartFolder;  // 是否为智能文件夹
    public string SmartFilter;  // 智能文件夹筛选条件 (JSON)
    public DateTime CreatedAt;
}
```

#### 标签 (Tag)
```csharp
public class ResourceTag
{
    public string Id;        // GUID
    public string Name;      // 标签名
    public string GroupName; // 标签分组
    public string Color;     // 颜色 (#hex)
    public int UsageCount;   // 使用次数（缓存字段）
}
```

### 4.4 SQLite 数据库表设计

```sql
-- 资源条目表
CREATE TABLE resources (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    file_name TEXT NOT NULL,
    relative_path TEXT,
    original_path TEXT,
    resource_type INTEGER NOT NULL,
    file_size INTEGER DEFAULT 0,
    file_hash TEXT,
    rating INTEGER DEFAULT 0,
    version TEXT,
    description TEXT,
    source_url TEXT,
    unity_version_min TEXT,
    unity_version_max TEXT,
    thumbnail_path TEXT,
    is_reference INTEGER DEFAULT 0,
    imported_at TEXT NOT NULL,
    file_modified_at TEXT,
    folder_id TEXT,
    FOREIGN KEY (folder_id) REFERENCES folders(id)
);

-- 文件夹表
CREATE TABLE folders (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    parent_id TEXT,
    sort_order INTEGER DEFAULT 0,
    color_tag TEXT,
    is_smart_folder INTEGER DEFAULT 0,
    smart_filter TEXT,
    created_at TEXT NOT NULL,
    FOREIGN KEY (parent_id) REFERENCES folders(id)
);

-- 标签表
CREATE TABLE tags (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    group_name TEXT,
    color TEXT,
    usage_count INTEGER DEFAULT 0
);

-- 资源-标签关联表
CREATE TABLE resource_tags (
    resource_id TEXT NOT NULL,
    tag_id TEXT NOT NULL,
    PRIMARY KEY (resource_id, tag_id),
    FOREIGN KEY (resource_id) REFERENCES resources(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tags(id) ON DELETE CASCADE
);

-- 全文搜索虚拟表
CREATE VIRTUAL TABLE resources_fts USING fts5(
    name, file_name, description, version,
    content='resources',
    content_rowid='rowid'
);

-- 索引
CREATE INDEX idx_resources_type ON resources(resource_type);
CREATE INDEX idx_resources_folder ON resources(folder_id);
CREATE INDEX idx_resources_rating ON resources(rating);
CREATE INDEX idx_resources_imported ON resources(imported_at);
CREATE INDEX idx_tags_group ON tags(group_name);
```

### 4.5 目录结构

```
Assets/
└── MagicBox/
    ├── Editor/
    │   ├── Core/
    │   │   ├── MagicBoxManager.cs          // 主管理器（单例）
    │   │   ├── LibraryDatabase.cs          // SQLite 数据库操作
    │   │   ├── ResourceLibraryManager.cs   // 资源库管理
    │   │   └── FileOperations.cs           // 文件操作工具
    │   │
    │   ├── Models/
    │   │   ├── ResourceItem.cs             // 资源条目模型
    │   │   ├── ResourceFolder.cs           // 文件夹模型
    │   │   ├── ResourceTag.cs              // 标签模型
    │   │   ├── ResourceLibrary.cs          // 资源库模型
    │   │   ├── ResourceType.cs             // 类型枚举
    │   │   └── SmartFilterRule.cs          // 智能文件夹规则
    │   │
    │   ├── Services/
    │   │   ├── ImportService.cs            // 资源导入服务
    │   │   ├── SearchService.cs            // 搜索与筛选服务
    │   │   ├── TagService.cs               // 标签管理服务
    │   │   ├── FolderService.cs            // 文件夹管理服务
    │   │   ├── ThumbnailService.cs         // 缩略图生成服务
    │   │   └── ExportService.cs            // 导出/导入项目服务
    │   │
    │   ├── UI/
    │   │   ├── Windows/
    │   │   │   ├── MagicBoxWindow.cs       // 主窗口
    │   │   │   ├── ImportWizardWindow.cs   // 导入向导窗口
    │   │   │   ├── TagManagerWindow.cs     // 标签管理窗口
    │   │   │   └── LibrarySettingsWindow.cs// 库设置窗口
    │   │   │
    │   │   ├── Views/
    │   │   │   ├── FolderTreeView.cs       // 左侧文件夹树
    │   │   │   ├── ResourceGridView.cs     // 网格视图
    │   │   │   ├── ResourceListView.cs     // 列表视图
    │   │   │   ├── ResourceDetailView.cs   // 详情面板
    │   │   │   ├── SearchBarView.cs        // 搜索栏
    │   │   │   ├── TagPanelView.cs         // 标签面板
    │   │   │   └── ToolbarView.cs          // 顶部工具栏
    │   │   │
    │   │   └── Styles/
    │   │       ├── MagicBoxMain.uss        // 主样式表
    │   │       ├── FolderTree.uss          // 文件夹树样式
    │   │       ├── ResourceCard.uss        // 资源卡片样式
    │   │       └── Common.uss              // 通用样式
    │   │
    │   ├── ThirdParty/
    │   │   └── SQLite/
    │   │       ├── Mono.Data.Sqlite.dll    // SQLite 库
    │   │       └── sqlite3.dll             // SQLite 原生库
    │   │
    │   └── Resources/
    │       ├── Icons/                      // 内置图标资源
    │       └── DefaultThumbnails/          // 默认缩略图
    │
    └── README.md                           // 使用说明（仅在发布时）
```

---

## 五、开发阶段规划

### Phase 1: 基础框架 (Foundation)
- [ ] 项目结构搭建、SQLite 集成
- [ ] 数据模型定义与数据库初始化
- [ ] 资源库创建/打开/切换
- [ ] 基础 EditorWindow 框架（空壳 + 布局分区）

### Phase 2: 核心功能 (Core Features)
- [ ] 文件夹树（创建、重命名、删除、拖拽排序）
- [ ] 资源导入（拖拽 + 文件选择 + 文件夹扫描）
- [ ] 资源类型自动识别
- [ ] 网格视图 + 列表视图
- [ ] 资源详情面板（查看/编辑元数据）

### Phase 3: 标签与搜索 (Tags & Search)
- [ ] 标签 CRUD + 标签分组
- [ ] 资源打标签（单个/批量）
- [ ] 搜索功能（名称 + 标签 + 类型）
- [ ] 组合筛选器
- [ ] 排序功能

### Phase 4: 操作与集成 (Actions & Integration)
- [ ] 一键导入 .unitypackage 到当前项目
- [ ] 复制资源到指定路径
- [ ] 打开文件/打开所在文件夹
- [ ] 批量操作（批量打标签、移动、删除）
- [ ] 右键上下文菜单

### Phase 5: 高级功能 (Advanced)
- [ ] 智能文件夹（条件自动过滤）
- [ ] 缩略图生成/自定义预览
- [ ] 星级评分筛选
- [ ] 引用模式（大文件不复制）
- [ ] 导入/导出库数据（JSON 备份）

### Phase 6: 优化与打磨 (Polish)
- [ ] 性能优化（大量资源的虚拟滚动）
- [ ] 快捷键支持
- [ ] 拖拽体验优化
- [ ] 打包为独立 .unitypackage 分发
- [ ] 使用文档

---

## 六、技术风险与对策

| 风险 | 对策 |
|------|------|
| SQLite 在 Unity 中的兼容性 | 使用 Mono.Data.Sqlite（Unity 内置），或备选 LiteDB（纯 C# 实现，零依赖） |
| UI Toolkit 学习曲线 | 核心交互用 UI Toolkit，复杂控件可混合 IMGUI |
| 大量资源时的性能 | 虚拟滚动（仅渲染可见项）、分页加载、异步缩略图生成 |
| 跨平台路径差异 | 统一使用 Path.Combine，路径存储时规范化为正斜杠 |
| 缩略图生成耗时 | 异步生成 + 缓存机制，首次显示占位图 |

---

## 七、备选方案对比

| 方案 | 优点 | 缺点 | 推荐度 |
|------|------|------|--------|
| **Unity Editor Extension** | 无缝集成 Unity 工作流；一键导入包；无需额外安装 | 只能在 Unity 编辑器中使用 | ★★★★★ |
| Electron 桌面应用 | 界面自由度高；独立于 Unity | 需要额外开发维护；无法直接导入包到 Unity | ★★★ |
| WPF/WinForms 桌面应用 | Windows 原生性能好 | 仅 Windows；无 Unity 集成 | ★★ |
| Web 应用 (本地服务) | 跨平台；现代 UI | 架构复杂；需启动服务 | ★★ |

**推荐方案：Unity Editor Extension**
理由：你的核心需求是管理 Unity 开发资源并快速导入项目，Editor Extension 是集成度最高、使用最方便的方案。它可以打包为 .unitypackage，在任何项目中一键导入即可使用。

---

## 八、可扩展方向（未来）

以下功能不在当前开发范围内，但架构设计时会预留扩展性：

1. **Asset Store 集成** - 关联 Unity Asset Store 购买记录
2. **Git 集成** - 支持从 Git 仓库拉取资源
3. **团队共享** - 多人共享资源库（通过网络盘或 NAS）
4. **自动更新检查** - 检查 Asset Store 资源是否有新版本
5. **资源依赖分析** - 分析 .unitypackage 内容和依赖关系
6. **AI 标签推荐** - 基于文件名/内容自动推荐标签

---

## 九、确认清单

请确认以下关键决策：

- [ ] **技术方案**：Unity Editor Extension + UI Toolkit + SQLite，是否同意？
- [ ] **数据库方案**：SQLite vs LiteDB（纯 C#，更简单但查询能力弱一些），倾向哪个？
- [ ] **UI 框架**：UI Toolkit 为主 vs 纯 IMGUI（UI Toolkit 更现代但学习成本高），倾向哪个？
- [ ] **资源存储**：是否需要"引用模式"（大文件只记录路径不复制）？
- [ ] **开发优先级**：Phase 1-6 的顺序是否合理，是否有功能需要提前或推后？
- [ ] **其他需求**：是否还有截图中未覆盖的资源类型或使用场景？
