# Skill 和 MCP 完全指南

> 适合 AI 初学者的详细教程

---

## 目录

- [1. 什么是 Skill](#1-什么是-skill)
- [2. Skill 和 MCP 的核心区别](#2-skill-和-mcp-的核心区别)
- [3. 使用场景和好处](#3-使用场景和好处)
- [4. 如何创建 Skill（初学者指南）](#4-如何创建-skill初学者指南)
- [5. 针对 SoraDevTools 项目的建议](#5-针对-soradevtools-项目的建议)
- [6. 常见问题](#6-常见问题)

---

## 1. 什么是 Skill

**Skill** 是 Claude Code 中对 AI 助手能力的扩展。简单说：**Skill 就是一个包含特定指令的文件**，告诉 Claude 在什么情况下应该如何工作。

### 比喻理解

想象你教一个新员工工作流程：
- 你写一份操作手册（`.md` 文件）
- 员工按照手册执行任务
- 每次遇到类似情况，都按同样的步骤做

### Skill 的工作流程

```
SKILL.md 文件 = 指令 + 元数据配置
   ↓
Claude 在适当时机自动加载或手动触发
   ↓
Claude 按照指令执行任务
```

### Skill 的本质

- 📄 **文件形式**：一个 `.md` Markdown 文件
- 📍 **存放位置**：`.claude/skills/` 目录
- 🎯 **作用**：定义 Claude 的工作方式和流程
- ⚡ **触发方式**：手动 `/skill-name` 或自动匹配

---

## 2. Skill 和 MCP 的核心区别

这是初学者最容易混淆的地方。

### 对比表格

| 方面 | Skill | MCP |
|------|-------|-----|
| **是什么** | 一份指令文档 | 一个外部工具或服务的连接器 |
| **如何工作** | Claude 读取 `.md` 文件中的指令，然后根据指令行动 | Claude Code 连接到外部系统（GitHub、数据库等），获取实时数据和功能 |
| **主要作用** | 改变 Claude 的**工作方式和思维方式** | 给 Claude 访问**外部工具和数据** |
| **例子** | "解释代码时用图表和类比" | GitHub API 工具、Sentry 监控、数据库查询 |
| **是否需要外部服务** | 否（纯本地） | 是（需要连接外部服务） |
| **配置位置** | `.claude/skills/` 目录 | `.mcp.json` 或 `~/.claude.json` |
| **触发方式** | 自动（Claude 判断）或手动（`/skill-name`） | Claude 在需要时调用工具 |
| **比喻** | 教 Claude 一套工作**流程和风格** | 给 Claude 一副**工具箱** |

### 生活中的类比

- **Skill**：教厨师一个新菜谱（"先炒葱姜，再加肉，最后调味"）
- **MCP**：给厨师提供外卖 APP（能订到平时没有的食材）

### 举例对比

**Skill 例子**：
```
用户问："这段代码怎么工作？"
↓
Claude 看到 /explain-code skill 的描述匹配
↓
Claude 加载 skill 指令："要用类比、ASCII 图表、逐步讲解"
↓
Claude 按照这个方式给出回答
```

**MCP 例子**：
```
用户问："查看我最近的 GitHub PR"
↓
Claude 调用 GitHub MCP 工具（获取实时数据）
↓
MCP 连接 GitHub API，返回真实的 PR 列表
↓
Claude 分析数据并回答
```

### Skill + MCP 结合使用

最强大的用法是**一起用**：

```
Skill（告诉 Claude 怎么做）+ MCP（给 Claude 工具和数据）
↓
既能按特定风格工作，又能访问实时数据
```

**示例**：一个 `deep-research` skill，它会：
1. 使用 Skill 指令：搜索文件 → 分析代码 → 总结发现
2. 使用 GitHub MCP：获取实际的 PR、issue、提交历史
3. 使用数据库 MCP：查询用户和分析数据

---

## 3. 使用场景和好处

### Skill 适合的场景

#### 1. 统一工作风格
让 Claude 每次都用你喜欢的方式工作：
- "代码审查时要检查的 5 个要点"
- "技术文档要包含代码示例和常见问题"

#### 2. 重复的工作流程
把复杂的步骤固定下来：
- 部署流程：测试 → 构建 → 推送
- Bug 修复：读 issue → 理解需求 → 实现 → 写测试

#### 3. 特定领域知识
教 Claude 你的项目习惯：
- 公司的 API 设计规范
- Unity 项目的最佳实践
- MagicBox 的架构约定

#### 4. 自动触发
在合适的时刻 Claude 自动使用：
- 用户问"怎么理解这个？" → 自动用 /explain-code skill

### Skill 的好处

✅ **节省 token** - 指令不用每次都输入
✅ **保证一致性** - 同样的任务总是同样的质量
✅ **可复用** - 跨项目使用或分享
✅ **用户友好** - `/skill-name` 像 Linux 命令

### MCP 适合的场景

#### 1. 访问实时数据
查询数据库、API、外部服务：
- "查询我的 PostgreSQL 用户表"
- "看最新的生产错误日志"
- "读取 Unity Console 输出"

#### 2. 集成外部工具
与工作流工具连接：
- GitHub：获取 PR、创建 issue
- Slack：发送通知
- Sentry：监控错误
- Unity Editor：操作场景和资源

#### 3. 自动化复杂操作
执行需要外部系统的任务：
- "创建 PR 并在 Slack 通知团队"
- "查询数据库并生成报告"

### MCP 的好处

✅ **实时访问** - 外部数据和系统
✅ **自动化** - 跨系统的工作流
✅ **扩展能力** - Claude 可以完成单靠 Skill 无法做的事

---

## 4. 如何创建 Skill（初学者指南）

### 4.1 最简单的例子：问候 Skill

**第 1 步**：创建文件夹
```bash
mkdir -p .claude/skills/greeting
```

**第 2 步**：创建 `SKILL.md`

在 `.claude/skills/greeting/SKILL.md` 中写入：

```markdown
---
name: greeting
description: Greet the user in a friendly way with a quote
---

When greeting the user, include:
1. A warm hello message
2. An inspiring quote about coding or development
3. Ask what they'd like to help with today
```

**第 3 步**：测试
```
用户：/greeting
↓
Claude 加载这个 skill 并按照指令给出温暖的问候
```

### 4.2 中等难度：代码审查 Skill

创建 `.claude/skills/code-review/SKILL.md`：

```yaml
---
name: code-review
description: Review code for quality, security, and best practices. Use when reviewing pull requests or code changes.
disable-model-invocation: false
allowed-tools: Read, Grep, Glob
---

# Code Review Checklist

When reviewing code, check these 5 areas:

## 1. Functionality
- Does the code solve the stated problem?
- Are there edge cases not handled?
- Is the logic correct?

## 2. Performance
- Any N+1 queries or inefficient loops?
- Could this be optimized?

## 3. Security
- Any hardcoded secrets or credentials?
- Proper input validation?
- SQL injection risks?

## 4. Maintainability
- Is the code readable?
- Are variable names clear?
- Are there complex sections that need comments?

## 5. Testing
- Are there sufficient test cases?
- Edge cases covered?

## Output Format

Provide:
1. Summary (✅ LGTM or ⚠️ Issues found)
2. Issues organized by category
3. Specific line numbers and suggestions
```

**使用方式**：
```
/code-review Assets/MagicBox/Scripts/Services/LibraryManager.cs
```

Claude 会按照 5 个检查点来审查你的代码。

### 4.3 高级例子：动态内容 Skill（适用于 UnityMCP 项目）

创建 `.claude/skills/unity-debug/SKILL.md`：

```yaml
---
name: unity-debug
description: Debug Unity Editor issues using MCP tools
context: fork
agent: Explore
---

# Unity Debugging Assistant

Debug the Unity Editor issue described: $ARGUMENTS

## Steps:
1. Read recent console output from the MCP server
2. Find related log files in the project
3. Identify error patterns
4. Suggest fixes

Use the UnityMCP tools to:
- Read console logs: `mcp__UnityMCP__read_console`
- Manage scripts: `mcp__UnityMCP__manage_script`
```

**关键点**：
- `$ARGUMENTS` = 用户传入的参数（例如 `/unity-debug "shader compilation error"` 里的 "shader compilation error"）
- `context: fork` = 在隔离的子 Agent 中运行
- `agent: Explore` = 使用专为探索优化的 Agent

### 4.4 带参数的 Skill

创建 `.claude/skills/migrate-component/SKILL.md`：

```yaml
---
name: migrate-component
description: Migrate a component from one framework to another
argument-hint: "[component-name] [from-framework] [to-framework]"
---

# Component Migration

Migrate the **$0** component from **$1** to **$2**.

## Process:
1. Read the current component: `$0`
2. Understand $1 API and patterns
3. Implement equivalent using $2
4. Preserve all tests
5. Update imports and dependencies

## Checklist:
- ✅ All tests pass
- ✅ Props/interfaces match
- ✅ Event handling works
- ✅ Styling preserved
```

**使用方式**：
```
/migrate-component SearchBar React Vue
```

Claude 获得：
- `$ARGUMENTS` = "SearchBar React Vue"
- `$0` = "SearchBar"
- `$1` = "React"
- `$2` = "Vue"

### 4.5 Skill 配置选项说明

在 Skill 文件的 frontmatter（`---` 之间的部分）中可以配置：

| 选项 | 说明 | 默认值 |
|------|------|--------|
| `name` | Skill 名称（必填） | 无 |
| `description` | Skill 描述（必填） | 无 |
| `disable-model-invocation` | 禁止 Claude 自动触发 | `false` |
| `user-invocable` | 允许用户手动调用 | `true` |
| `allowed-tools` | 允许使用的工具列表 | 所有工具 |
| `argument-hint` | 参数提示 | 无 |
| `context` | 执行上下文（`fork` = 子进程） | 当前上下文 |
| `agent` | 使用的 Agent 类型 | `general-purpose` |

---

## 5. 针对 SoraDevTools 项目的建议

根据你的 `CLAUDE.md` 和项目结构，建议创建以下 Skill：

### 建议 1️⃣：MagicBox 开发规范 Skill

创建 `.claude/skills/magicbox-dev/SKILL.md`：

```yaml
---
name: magicbox-dev
description: Assist with MagicBox development following project conventions
allowed-tools: Read, Edit, Write, Grep, Glob
---

# MagicBox 开发助手

在开发 MagicBox 功能时，请遵循以下规范：

## 架构层次
```
UI Layer (UI Toolkit: UXML/USS)
    ↓
Service Layer (LibraryManager, ImportService, SearchService, TagService, FolderService)
    ↓
Data Layer (LibraryDatabase → SQLiteConnection)
```

## 命名空间规则
- 核心功能：`MagicBox.Core` - 数据库、设置、SQLite、本地化
- 数据模型：`MagicBox.Models` - ResourceItem, ResourceFolder, ResourceTag 等
- 业务逻辑：`MagicBox.Services` - 所有服务类
- 界面相关：`MagicBox.UI` - Views 和 UI 组件

## UI 开发规范
- ✅ 使用 UI Toolkit (UXML/USS)
- ❌ 不要使用旧的 IMGUI
- 样式文件位置：`Assets/MagicBox/UI/Styles/`

## 服务类规范
- 所有服务都是**单例模式**
- 通过 `MagicBoxApp` 初始化
- 核心服务：LibraryManager, AppSettings, LibraryDatabase

## 数据库规范
- 使用 SQLite
- 4 个表：`resources`, `folders`, `tags`, `resource_tags`
- **必须使用参数化查询**防止 SQL 注入
- Schema 由 `LibraryDatabase` 自动创建

## 资源类型
15 种类型定义在 `ResourceType` 枚举中：
- UnityPackage, ZipArchive, CSharpScript, ShaderFile
- Prefab, Material, Texture, Model3D
- Audio, Animation, Font, Document
- Executable, Folder, Other

## 代码审查要点
开发完成后检查：
1. ✅ 是否使用了正确的命名空间？
2. ✅ UI 代码是否使用了 UI Toolkit？
3. ✅ 服务类是否正确实现单例？
4. ✅ SQL 查询是否使用了参数化？
5. ✅ 是否有安全问题（SQL 注入、XSS、路径遍历等）？
6. ✅ Win32 API 调用是否正确处理了异常？

## 关键入口点
- `Assets/MagicBox/Scripts/UI/MagicBoxApp.cs` - 应用程序入口
- `Assets/MagicBox/Scripts/Core/LibraryDatabase.cs` - 数据库操作
- `Assets/MagicBox/Scripts/Services/LibraryManager.cs` - 资源库管理
- `Assets/MagicBox/Editor/MagicBoxSetup.cs` - Editor 初始化
```

**使用方式**：
```
/magicbox-dev
帮我实现一个新的搜索过滤功能
```

### 建议 2️⃣：Unity 调试 Skill

创建 `.claude/skills/unity-debug/SKILL.md`：

```yaml
---
name: unity-debug
description: Debug Unity Editor issues step by step
allowed-tools: Read, Grep, Glob, Bash
---

# Unity 错误调试助手

系统化调试 Unity Editor 问题。

## 调试步骤

### 1. 收集信息
- 错误类型（编译错误 / 运行时错误 / 警告）
- 错误消息和堆栈跟踪
- 发生的具体操作

### 2. 定位源头
- 找到出错的脚本文件和行号
- 检查相关的依赖和引用
- 查看最近的修改记录

### 3. 常见问题检查
- ✅ 空引用（NullReferenceException）
- ✅ 路径错误（资源未找到）
- ✅ 类型不匹配（Type mismatch）
- ✅ 序列化问题（Missing script reference）
- ✅ 编译错误（语法错误、命名空间缺失）

### 4. 提供解决方案
- 具体的修复代码
- 解释问题原因
- 防止未来再次发生的建议

### 5. 验证修复
- 建议测试步骤
- 检查相关功能是否受影响
```

### 建议 3️⃣：SQLite 查询助手 Skill

创建 `.claude/skills/sqlite-helper/SKILL.md`：

```yaml
---
name: sqlite-helper
description: Help write and optimize SQLite queries for MagicBox
allowed-tools: Read, Grep
---

# SQLite 查询助手

帮助编写安全、高效的 SQLite 查询。

## MagicBox 数据库 Schema

### 表结构
- `resources` - 资源条目（id, name, path, description, resource_type, folder_id, rating, tags, imported_at, file_size, checksum）
- `folders` - 文件夹（id, name, parent_id, path, color, icon）
- `tags` - 标签（id, name, color, category）
- `resource_tags` - 资源-标签关联（resource_id, tag_id）

### 索引
- `resource_type`, `folder_id`, `rating`, `imported_at`

## 编写查询规范

### 1. 必须使用参数化查询
❌ 错误：
```csharp
string sql = $"SELECT * FROM resources WHERE name = '{name}'";
```

✅ 正确：
```csharp
string sql = "SELECT * FROM resources WHERE name = ?";
connection.Query<ResourceItem>(sql, name);
```

### 2. 利用索引优化
优先使用已建立索引的字段：
- 按类型筛选：`WHERE resource_type = ?`
- 按文件夹筛选：`WHERE folder_id = ?`
- 按评分排序：`ORDER BY rating DESC`
- 按时间排序：`ORDER BY imported_at DESC`

### 3. JOIN 操作
获取资源的标签：
```sql
SELECT r.*, GROUP_CONCAT(t.name) as tag_names
FROM resources r
LEFT JOIN resource_tags rt ON r.id = rt.resource_id
LEFT JOIN tags t ON rt.tag_id = t.id
WHERE r.id = ?
GROUP BY r.id
```

### 4. 全文搜索
使用 LIKE 模糊搜索（已在代码中实现）：
```sql
WHERE name LIKE ? OR description LIKE ?
```

## 输出格式
提供：
1. 完整的 SQL 查询
2. C# 调用代码示例
3. 性能说明（是否利用索引）
4. 潜在的安全问题警告
```

### 建议 4️⃣：代码解释 Skill（适合学习）

创建 `.claude/skills/explain-simple/SKILL.md`：

```yaml
---
name: explain-simple
description: Explain code in simple language with diagrams
allowed-tools: Read, Grep
---

# 代码简单讲解

用最通俗的语言解释代码，适合学习和理解。

## 讲解格式

### 1. 功能概述（一句话）
用日常语言说明这段代码做什么（避免专业术语）

### 2. 流程图（ASCII）
画一个简单的流程图展示执行步骤

### 3. 生活类比
举一个生活中的例子来类比代码逻辑

### 4. 逐行解释
- 关键变量的作用
- 每个步骤在做什么
- 为什么要这样写

### 5. 注意事项
- 可能出错的地方
- 性能考虑
- 安全问题

## 示例

对于这段代码：
```csharp
public ResourceItem GetById(int id)
{
    return connection.Find<ResourceItem>(id);
}
```

### 功能概述
根据 ID 从数据库中找到一个资源

### 流程图
```
用户提供 ID
    ↓
在数据库中查找
    ↓
返回找到的资源（或 null）
```

### 生活类比
就像在图书馆用书号找书：你告诉管理员书号，管理员去书架上找这本书并拿给你。

### 逐行解释
- `ResourceItem` - 资源对象（像一本书的信息卡片）
- `GetById(int id)` - 通过 ID 查找的方法
- `connection.Find<ResourceItem>(id)` - 在数据库连接中查找
  - `connection` = 数据库连接（像图书馆的目录系统）
  - `Find` = 查找操作
  - `id` = 要找的资源编号

### 注意事项
- 如果 ID 不存在，返回 `null`，调用者需要检查
- 这是同步操作，大量查询可能影响性能
```

---

## 6. 常见问题

### Q1: Skill 和直接给 Claude 发指令有什么区别？

**A:**
- Skill 可以**重复使用**，不用每次都输入
- Skill 可以被 Claude **自动触发**（当它识别到合适的场景）
- Skill 保存在文件中，可以**版本控制**和**团队共享**
- Skill 更**节省 token**（不会重复计入上下文）

### Q2: 我需要编程基础才能创建 Skill 吗？

**A:** 不需要！Skill 就是一个文本文件，用普通话（或英文）写清楚"怎么做"就行。就像写一份操作手册。

### Q3: Skill 和 MCP 能一起用吗？

**A:** 能！这是最强大的用法：
- **Skill** 定义"怎么做"（比如代码审查的步骤）
- **MCP** 提供数据和工具（比如从 GitHub 获取 PR，从 Unity 获取 Console 日志）
- 两者结合 = 既有流程又有数据

### Q4: 我的 MCP 工具在哪里配置？

**A:** 在你的项目中：
- MCP 服务器配置：`.claude/settings.local.json`
- 已配置的 MCP：`UnityMCP`（在 `Packages/MCPForUnity/`）
- MCP 允许的工具在 `settings.local.json` 中的 `mcpServers.UnityMCP.allow` 定义

### Q5: Skill 文件放在哪里？

**A:**
- 项目级别：`.claude/skills/` 目录
- 全局级别：`~/.claude/skills/` 目录
- 推荐使用项目级别，方便团队共享和版本控制

### Q6: Skill 能访问项目文件吗？

**A:** 能！通过 `allowed-tools` 配置，Skill 可以使用：
- `Read` - 读取文件
- `Write` - 写入文件
- `Edit` - 编辑文件
- `Grep` - 搜索内容
- `Glob` - 查找文件
- `Bash` - 执行命令

### Q7: 如何调试 Skill？

**A:**
1. 在 Claude Code 中手动调用：`/skill-name`
2. 查看 Claude 的输出是否符合预期
3. 如果不符合，修改 `SKILL.md` 文件
4. 重新调用测试
5. 可以在 Skill 中加入"输出格式"要求来验证

### Q8: Skill 能自动触发吗？

**A:** 能！如果你设置：
```yaml
---
disable-model-invocation: false
---
```

Claude 会在识别到合适的场景时自动加载这个 Skill。

如果设置为 `true`，则只能手动调用（`/skill-name`）。

### Q9: Skill 支持多语言吗？

**A:** 支持！你可以用中文或英文（或混合）编写 Skill 内容。Claude 都能理解。

### Q10: 多个 Skill 能同时使用吗？

**A:** 能！Claude 可以根据需要加载多个 Skill。比如：
- 用 `/magicbox-dev` 加载开发规范
- 再用 `/code-review` 进行代码审查
- 两个 Skill 的指令会同时生效

---

## 快速参考

### 最小化 Skill 模板

```markdown
---
name: my-skill
description: What this skill does
---

Your instructions here...
```

### 完整 Skill 模板

```markdown
---
name: my-skill
description: When to use this skill
disable-model-invocation: false    # Claude 能否自动触发
user-invocable: true               # 用户能否手动调用
allowed-tools: Read, Grep, Glob    # 允许使用哪些工具
argument-hint: "[file] [format]"   # 参数提示
context: fork                      # 执行上下文
agent: Explore                     # Agent 类型
---

# Skill 标题

Your detailed instructions...

## 使用参数

参数可用为：
- $ARGUMENTS：所有参数
- $0, $1, $2...：第 1、2、3 个参数
```

### Skill vs MCP 决策树

```
需要改变 Claude 的工作方式？
   ↓
  是 → 使用 Skill
   ↓
  否
   ↓
需要访问外部数据或工具？
   ↓
  是 → 使用 MCP
   ↓
  否
   ↓
都需要？
   ↓
  是 → Skill + MCP 组合使用
```

---

## 下一步行动

根据 SoraDevTools 项目，建议你：

1. ✅ **立即创建** - MagicBox 开发规范 Skill
2. ✅ **优化现有** - 检查 `.claude/skills/` 是否有现成的 skill 可以改进
3. ✅ **结合 UnityMCP** - 创建调试 Skill，利用 UnityMCP 工具
4. ✅ **团队分享** - 将 Skill 提交到 Git，让团队成员共享

---

## 总结

```
┌─────────────────────────────────────────────┐
│        Skills vs MCP 关键区别                │
├─────────────────────────────────────────────┤
│ Skill: 改变 Claude 的思维方式                │
│        教它新的工作流程和风格                │
│        本地、无需外部连接                    │
│                                             │
│ MCP:   给 Claude 新的工具和数据              │
│        连接到外部系统（API、数据库等）      │
│        需要实时交互                         │
└─────────────────────────────────────────────┘
```

**记住**：Skill 是"教"，MCP 是"给"。两者结合，让 Claude 既懂怎么做，又有工具做！

---

**文档版本**：v1.0
**创建日期**：2026-02-20
**适用项目**：SoraDevTools (Unity 2022.3.56f1)
