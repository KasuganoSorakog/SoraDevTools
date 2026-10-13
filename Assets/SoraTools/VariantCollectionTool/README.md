# Shader Variant Collection Tool

Unity Editor 扩展，用于收集项目实际用到的 Shader 变体，生成 `.shadervariants` 文件，消除打包体积冗余与运行时编译卡顿。

---

## 目录

- [背景：为什么需要这个工具](#背景为什么需要这个工具)
- [功能特性](#功能特性)
- [打开工具](#打开工具)
- [两种收集模式](#两种收集模式)
- [完整使用流程](#完整使用流程)
- [Shader 变体剥离器](#shader-变体剥离器)
- [如何保证收集完整](#如何保证收集完整)
- [文件说明](#文件说明)
- [注意事项](#注意事项)
- [故障排除](#故障排除)
- [版本历史](#版本历史)

---

## 背景：为什么需要这个工具

一个 Shader 支持的每种关键字组合就是一个**变体**。例如一个支持法线贴图、阴影、雾效的 Shader 理论上有 8 种变体。

**不收集的后果：**
- Unity 打包时不确定哪些变体会被用到，将大量变体全部编译进包，**包体虚大**
- 游戏运行中遇到未预编译的变体时**实时编译，导致卡顿/卡帧**

**收集后的效果：**
- 只保留项目实际用到的变体，**减小包体**
- 游戏启动时提前预热（WarmUp），运行中**不再出现编译卡顿**

---

## 功能特性

- **文件夹模式**：扫描指定文件夹下所有 `.mat` 材质及预制体引用的材质
- **场景模式**：逐场景扫描 Build Settings 中的所有场景，生成每场景独立文件 + 合并总表
- **中英文切换**：界面语言实时切换，配置持久化保存
- **材质预览**：收集前预览将要处理的材质列表
- **结果统计面板**：收集完成后展示 Shader 数、变体数、耗时
- **JSON 清单导出**：生成可读性强的 `.json` 清单，便于版本对比
- **Shader 变体剥离器**：打包时按关键字剔除不需要的变体，提供 Project Settings 可视化管理界面
- **批量分批处理**：动态调整批次间等待时间，避免编辑器卡顿
- **场景自动恢复**：收集完成或取消后自动恢复原始场景

---

## 打开工具

```
Unity 菜单栏 → SoraTools → Shader Variant Collector
```

---

## 两种收集模式

### 文件夹模式（Folder）

适合：项目材质集中存放在特定目录，或想快速收集某个模块的变体。

| 配置项 | 说明 |
|--------|------|
| 材质文件夹 | 递归扫描该文件夹下所有 `.mat` 文件 |
| 包含预制体材质 | 同时扫描文件夹内预制体引用的材质（Renderer / ParticleSystem / UI）|
| 输出路径 | 生成单个 `.shadervariants` 文件的保存位置 |
| 批处理大小 | 每批处理的材质数量，推荐 10～20 |
| 导出 JSON 清单 | 同时生成同名 `.json` 文件 |

### 场景模式（Scene）

适合：项目按场景组织，希望为每个场景单独生成变体文件，同时生成全局总表。

| 配置项 | 说明 |
|--------|------|
| 输出文件夹 | 所有文件的保存目录 |
| 批处理大小 | 同上 |
| 导出 JSON 清单 | 同上 |

**输出结果：**
```
输出文件夹/
├── Scene_A.shadervariants      ← 场景 A 的变体
├── Scene_A.json
├── Scene_B.shadervariants      ← 场景 B 的变体
├── Scene_B.json
└── _Master.shadervariants      ← 所有场景合并的总表（实际使用这个）
    _Master.json
```

> 场景列表来自 **Build Settings → Scenes In Build**，只处理已勾选启用的场景。

---

## 完整使用流程

### 第一步：收集变体

1. 打开工具：`SoraTools → Shader Variant Collector`
2. 选择收集模式，配置路径和参数
3. （可选）点击"预览材质"确认扫描范围
4. 点击"开始收集"，等待完成

> **建议在开始前保存当前场景**（File → Save Scene），收集结束后工具会自动恢复原始场景。若场景未保存，工具会弹出提示。

### 第二步：配置预加载

收集完成后，将 `_Master.shadervariants`（场景模式）或输出的 `.shadervariants`（文件夹模式）添加到预加载列表：

```
Edit → Project Settings → Graphics → Shader Loading → Preloaded Shaders
```

将文件拖入列表，Unity 打包后会在游戏启动时自动 WarmUp 这些变体。

### 第三步：验证（可选）

打包后在目标平台运行，开启以下选项观察是否有遗漏变体被实时编译：

```
Project Settings → Graphics → Shader Loading → Log Shader Compilation: ✅
```

若 Console 中出现 "Shader xxx compiled" 日志，说明该变体未被预收集，需补充。

---

## Shader 变体剥离器

位于 `ShaderStripping/` 子目录，在**打包时**按关键字剔除不需要的变体，进一步减小包体。

> **注意**：剥离器作用于打包流程（`IPreprocessShaders`），不影响编辑器内的收集过程。

### 配置入口

```
Edit → Project Settings → Sora Tools → Shader Variant Stripper
```

| 选项 | 说明 |
|------|------|
| 打包时启用变体剥离 | 总开关，绿色=启用，红色=禁用 |
| 剥离关键字列表 | 包含列表中任意关键字的变体将在打包时被移除 |
| + 添加关键字 | 添加新的剥离关键字 |
| 恢复默认列表 | 恢复内置的 15 个 URP 常见无用关键字 |
| 定位 | 在 Project 窗口中定位配置文件 |

### 默认剥离的关键字

内置以下 URP 常见冗余关键字（可自定义增删）：

```
_SPECULARHIGHLIGHTS_OFF       _ENVIRONMENTREFLECTIONS_OFF
_PARALLAXMAP                  _ADDITIONAL_LIGHTS_VERTEX
_FORWARD_PLUS                 _SHADOWS_SOFT / _SOFT_LOW / _MEDIUM / _HIGH
_REFLECTION_PROBE_BLENDING    _REFLECTION_PROBE_BOX_PROJECTION
_SCREEN_SPACE_OCCLUSION       _LIGHT_LAYERS
LOD_FADE_CROSSFADE            DEBUG_DISPLAY
```

### 配置文件位置

```
Assets/SoraTools/VariantCollectionTool/Editor/ShaderStripping/ShaderVariantStripperSettings.asset
```

首次打开 Project Settings 页面时自动创建，可纳入版本控制。

---

## 如何保证收集完整

静态扫描（本工具）只能捕获**文件中已存在的材质**，以下情况需要补充：

| 遗漏场景 | 说明 | 解决方案 |
|----------|------|----------|
| 运行时动态加载 | 通过 Addressables / Resources / AssetBundle 加载的材质 | 运行时录制（见下） |
| 代码动态开关关键字 | `material.EnableKeyword(...)` | 运行时录制 |
| 光照条件变化 | 灯光数量变化、阴影模式切换 | 运行时录制 |
| 平台特定变体 | 仅在特定 GPU 或平台触发的变体 | 在目标设备上录制 |

### 运行时录制（最完整的补充方式）

1. 开启录制：`Project Settings → Graphics → Shader Loading → Log Shader Compilation ✅`
2. 在 Editor 中**完整通关一遍游戏**，触发所有场景、特效、UI 状态
3. 完成后点击 `Project Settings → Graphics → Shader Loading → Save to asset...`
4. 将录制结果与本工具生成的 `_Master` 合并，或直接用录制结果替换

**推荐组合策略：**
```
本工具静态扫描（覆盖已知材质资产）
       +
运行时录制（覆盖动态加载和代码触发）
       ↓
合并 → 最终 _Master.shadervariants → Preloaded Shaders
```

---

## 文件说明

```
VariantCollectionTool/
├── README.md
└── Editor/
    ├── ShaderVariantCollectorWindow.cs     # EditorWindow 界面（中英文/配置持久化/结果面板）
    ├── ShaderVariantCollectorCore.cs       # 核心收集逻辑（状态机 / 批处理 / 场景管理）
    ├── ShaderVariantCollectionManifest.cs  # 数据结构与 JSON 序列化
    ├── ShaderVariantCollectionHelper.cs    # ShaderUtil 内部 API 反射封装
    └── ShaderStripping/
        ├── ShaderVariantStripper.cs        # IPreprocessShaders 实现（打包时剥离）
        ├── ShaderVariantStripperSettings.cs # ScriptableObject 配置 + Project Settings 面板
        └── ShaderVariantStripperSettings.asset  # 配置文件（首次使用后自动生成）
```

---

## 注意事项

1. **收集前保存场景**：收集过程会创建临时场景，完成后自动恢复。若当前场景未保存，工具会弹出警告，建议先保存。

2. **Game View 需可见**：收集依赖 Unity 渲染帧来触发变体记录，Game View 必须在屏幕上可见（不能被其他窗口完全遮挡）。

3. **Batch Size 调整建议**：
   - 材质数量少（< 200）：Batch Size 可设 20～50
   - 材质数量多（> 500）：Batch Size 建议 10～20，防止编辑器无响应

4. **场景模式需配置 Build Settings**：场景模式只处理 `Build Settings → Scenes In Build` 中已勾选的场景。

5. **剥离器与收集器互不影响**：剥离器仅在打包时生效，不影响编辑器内的收集结果。

6. **Unity 版本**：工具使用反射调用 `ShaderUtil` 内部 API，已在 Unity 2022.3 LTS 验证。其他版本如遇方法找不到，可在 `ShaderVariantCollectionHelper.cs` 中调整方法名。

---

## 故障排除

### 收集完成但变体数为 0

- 检查材质文件夹路径是否以 `Assets/` 开头
- 确认材质有效且 Shader 未丢失（Console 无 missing shader 警告）
- 确认 Game View 在收集期间可见

### 场景模式跳过某些场景

- 确认该场景已在 `Build Settings → Scenes In Build` 中勾选启用
- 检查场景中是否确实有带材质的 Renderer 组件

### 编辑器在收集时无响应

- 降低 Batch Size（建议 5～10）
- 大型项目可分文件夹多次收集，最终合并

### 收集完成后未恢复原始场景

- 若原始场景从未保存过（新建场景未执行 Save），工具无法恢复，此为预期行为
- 请在收集前执行 `File → Save Scene`

### Project Settings 中找不到 Shader Variant Stripper

- 打开后会自动创建配置文件，如未出现请重启 Unity Editor

---

## 版本历史

**v2.0**
- 新增场景模式（逐场景收集 + 自动合并 `_Master`）
- 新增中英文切换，配置持久化（EditorPrefs）
- 新增结果统计面板（Shader 数 / 变体数 / 耗时）
- 新增 Shader 变体剥离器（Project Settings 可视化管理）
- 修复：场景恢复逻辑（原始路径在入口处保存，避免多场景模式覆盖）
- 修复：`AreKeywordsEqual` 含重复关键字时的错误比较
- 修复：`Extract()` 变体计数从 manifest 本身统计，不依赖全局运行时集合
- 修复：`CollectBatch` Camera 为 null 时材质静默丢失
- 修复：`EditorApplication.update` 多次注册问题
- 修复：场景模式结束后 Game View 结果面板统计数为 0
- 修复：场景模式切换时临时场景触发脏数据保存对话框
- 性能：日志样式、统计面板样式缓存，避免每帧 GUIStyle 分配
- 性能：剥离器 Settings 和 HashSet 在 Build 期间只构建一次

**v1.0**
- 基础文件夹模式收集
- EditorWindow 界面
- JSON 清单导出
- 批处理模式
