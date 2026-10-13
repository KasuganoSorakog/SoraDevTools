# 如何减少打包后的Shader大小

## 第一步: 诊断——知己知彼

在优化之前，你需要知道具体是哪个Shader产生了大量变体。

1. **查看 Editor Log**：

   - 执行一次构建（Build）。

   - 打开 Console 窗口，点击右上角下拉菜单 -> **Open Editor Log**。

   - 在日志文件中搜索 `Compiling shader` 或 `Shader variants`。

   - 你会看到类似这样的信息：

     

     ```
     Compiling shader "Universal Render Pipeline/Lit" pass "ForwardLit" (program "fp", platform "d3d11")
     ...
     2304 variants compiled.
     ```

   - **行动**：找出变体数量最多的前几个Shader，它们是你优化的重点。

   - **此处可使用 `Build Report Tool` 插件分析打包日志**

2. **使用 Memory Profiler**：

   - 使用Unity的Memory Profiler包，查看Snapshot中的Shader占用情况，确认哪些Shader占据了大量内存。

------

## 第二步: 基础设置优化（无代码）

这是最快见效的方法，通过剔除项目中未使用的全局图形特性来减少变体。

1. **Render Pipeline Asset (SRP/URP/HDRP专用)**：
   - 如果你使用的是URP或HDRP，找到你的 `Pipeline Asset` 配置文件。
   - **Lighting**：如果不需要 `Main Light Shadows` 或 `Additional Lights`，关闭它们。
   - **Shadows**：关闭 `Soft Shadows` 可以减少变体。
   - **Post-processing**：禁用未使用的后处理效果。
2. **Always Included Shaders**：
   - 检查 `Project Settings` -> `Graphics` -> `Always Included Shaders` 列表。
   - **行动**：移除所有非必须的Shader。这里的Shader无论是否被引用，都会被强制包含所有变体打入包中，是“重灾区”。

## 第三步: 使用 `IPreprocessShaders` 进行“逻辑剔除”（高收益）

- **策略**：编写脚本剔除**“逻辑上不可能存在”**或**“项目中绝对不用”**的组合（黑名单模式）。
- 代码思路：
  - “我们项目是俯视角的，永远不会用到级联阴影（Cascades），剔除它。”
  - “在这个Shader下，不可能同时开启‘半透明’和‘雾效’，剔除这个组合。”
- **优点**：这是基于规则的，不需要跑游戏收集，100%安全且能减掉大头。

## 第四步: 变体收集（用于性能，而非剔除）

- **策略**：使用 `ShaderVariantCollection`。
- **目的**：在游戏启动时调用 `Warmup`，解决第一次渲染时的卡顿问题。

## 编写Shader时需要注意

- **策略**：尽可能多地使用 `#pragma shader_feature` 而不是 `multi_compile`。
- **原理**：Unity 能够在打包时自动扫描所有场景和Resources中的材质球（Material）。如果材质球没用到 `_EMISSION` 宏，Unity 自动就不打这个变体. 但是multi_compile 在没有额外剔除逻辑的情况下,默认会打包进去.

# 打包AB时,Shader的一个打包逻辑

打AssetBundle（AB包）时的Shader打包逻辑是Unity进阶开发中最容易“踩坑”的环节之一。

相比于直接打整包（Build Player），AB包的Shader打包逻辑有**三个核心差异**，如果不理解这些差异，极易导致**包体冗余**或**运行时材质变粉（丢失变体）**。

以下是详细的逻辑解析：

------

### 1. 依赖与冗余逻辑：Shader会被打进哪里？

Unity的AB打包遵循“依赖树”原则。

- **情况 A：隐式依赖（糟糕的情况）**
  - **场景**：你有 `Material_A`（在 ab_a 中）和 `Material_B`（在 ab_b 中），它们都使用了 `Shader_X`。但是，你**没有**显式地将 `Shader_X` 分配给任何一个AB包。
  - **逻辑**：Unity在打 `ab_a` 时，发现需要 `Shader_X`，于是把 `Shader_X` 的代码（及变体）完整拷贝一份打进 `ab_a`。打 `ab_b` 时，又完整拷贝一份打进 `ab_b`。
  - **后果**：Shader体积成倍膨胀。如果你的Shader有2MB，两个包就浪费了2MB。
- **情况 B：显式依赖（最佳实践）**
  - **场景**：你将 `Shader_X` 单独设置了一个AB包名（例如 `shader_core`）。
  - **逻辑**：Unity打 `shader_core` 时包含Shader本体。打 `ab_a` 和 `ab_b` 时，只会记录“引用了 `shader_core` 里的 `Shader_X`”。
  - **后果**：Shader只有一份。加载时需要先加载 `shader_core`，再加载 `ab_a`。

------

### 2. 变体剔除逻辑：`shader_feature` 的致命陷阱

这是AB包Shader最容易出问题的地方。

- **回顾**：我们之前说过，`#pragma shader_feature` 是根据“当前构建中材质球的引用情况”来决定打包哪些变体。
- **AB包的陷阱**：当你单独打包一个 `shader_core` AB包时，Unity**只看这个包里有什么**。
  - 如果 `shader_core` 包里**只有** `.shader` 文件，而**没有**引用该Shader的材质球（Material）。
  - **逻辑**：Unity扫描 `shader_core`，发现没有任何材质球使用了 `KEYWORD_A`。
  - **结果**：Unity认为 `KEYWORD_A` 没被用到，直接剔除。
  - **灾难**：当你运行时加载了 `ab_a`（里面的材质球需要 `KEYWORD_A`），由于 `shader_core` 里根本没打进这个变体，物体直接变粉。
- **解决方案：ShaderVariantCollection (SVC)**
  - 你必须创建一个 `ShaderVariantCollection` 资源，把所有需要用到的变体组合记录进去。
  - **关键步骤**：将这个 SVC 文件也打进 `shader_core` 包（或者确保引用关系）。
  - **逻辑修正**：Unity在打包 `shader_core` 时，会查看 SVC 列表，发现“虽然没有材质球用到，但 SVC 要求保留这些变体”，于是强制把这些变体打进去。

------

### 3. `multi_compile` 的打包逻辑

- **逻辑**：对于 `#pragma multi_compile`，AB包的处理逻辑和打整包**完全一致**。
- 不管有没有材质球引用，也不管有没有 SVC，只要代码里写了，默认全打进去。
- **注意**：`IPreprocessShaders` 脚本依然有效。你可以在脚本里根据 `BuildTarget` 或其他逻辑，在打AB包时剔除 `multi_compile` 的某些变体。

------

### 4. 最佳实践流程图

为了确保Shader在AB包中既不冗余也不丢失，请严格遵循以下流程：

#### 步骤一：独立打包 Shader

将所有项目中的 Shader 文件（以及下文提到的 SVC）归类到一个专门的 AB 包中，例如命名为 `shaders_all`。

- **目的**：避免 Shader 代码在各个玩法模块的 AB 包中重复出现。

#### 步骤二：收集变体 (解决 shader_feature 问题)

1. 使用工具遍历所有 Prefab 和 Material，分析出所有用到的 `shader_feature` 组合。

2. 生成一个 `ProjectShaders.shadervariants` (SVC) 文件。

3. 重要

   ：将这个 SVC 文件放入`shaders_all`包中，或者确保`shaders_all`包含的某个资源引用了这个 SVC。

   - *技巧*：如果不把 SVC 显式打进去，Unity 可能还是会忽略它。最稳妥的方法是把 SVC 设置为 `shaders_all` 包。

#### 步骤三：加载顺序 (Runtime)

在游戏运行时：

1. 首先加载 `shaders_all` AssetBundle。
2. 加载 SVC 资源，并调用 `svc.WarmUp()` (可选，为了预热)。
3. 然后再加载其他的 `gameplay_weapon`、`gameplay_scene` 等包含材质球的 AB 包。

### 总结 Shader 在 AB 中的打包逻辑：

1. **归属权**：如果 Shader 没独立打名，会被“克隆”进每一个引用它的 AB 包（体积爆炸）。
2. **`multi_compile`**：无脑全进（除非用脚本剔除）。
3. **`shader_feature`**：**极其“短视”**。打包 Shader 所在的 AB 时，如果同一个包里没有材质球用到该 Feature，也没 SVC 声明，它就会被丢弃（导致变粉）。

**一句话建议**：
**Shader 必须独立打 AB 包，并且该包内必须包含一个记录了所有有效变体的 `ShaderVariantCollection`。**

Unity URP源码中对 PassType的定义

```
public enum PassType
{
  /// <summary>
  ///   <para>Regular shader pass that does not interact with lighting.</para>
  /// </summary>
  Normal = 0,
  /// <summary>
  ///   <para>Legacy vertex-lit shader pass.</para>
  /// </summary>
  Vertex = 1,
  /// <summary>
  ///   <para>Legacy vertex-lit shader pass, with mobile lightmaps.</para>
  /// </summary>
  VertexLM = 2,
  /// <summary>
  ///   <para>Legacy vertex-lit shader pass, with desktop (RGBM) lightmaps.</para>
  /// </summary>
  [Obsolete("VertexLMRGBM PassType is obsolete. Please use VertexLM PassType together with DecodeLightmap shader function.")] VertexLMRGBM = 3,
  /// <summary>
  ///   <para>Forward rendering base pass.</para>
  /// </summary>
  ForwardBase = 4,
  /// <summary>
  ///   <para>Forward rendering additive pixel light pass.</para>
  /// </summary>
  ForwardAdd = 5,
  [Obsolete("Deferred Lighting was removed, so LightPrePassBase pass type is never used anymore.")] LightPrePassBase = 6,
  [Obsolete("Deferred Lighting was removed, so LightPrePassFinal pass type is never used anymore.")] LightPrePassFinal = 7,
  /// <summary>
  ///   <para>Shadow caster &amp; depth texure shader pass.</para>
  /// </summary>
  ShadowCaster = 8,
  /// <summary>
  ///   <para>Deferred Shading shader pass.</para>
  /// </summary>
  Deferred = 10, // 0x0000000A
  /// <summary>
  ///   <para>Shader pass used to generate the albedo and emissive values used as input to lightmapping.</para>
  /// </summary>
  Meta = 11, // 0x0000000B
  /// <summary>
  ///   <para>Motion vector render pass.</para>
  /// </summary>
  MotionVectors = 12, // 0x0000000C
  /// <summary>
  ///   <para>Custom scriptable pipeline.</para>
  /// </summary>
  ScriptableRenderPipeline = 13, // 0x0000000D
  /// <summary>
  ///   <para>Custom scriptable pipeline when lightmode is set to default unlit or no light mode is set.</para>
  /// </summary>
  ScriptableRenderPipelineDefaultUnlit = 14, // 0x0000000E
  /// <summary>
  ///   <para>Grab Pass. Use this when you want to detect the type of the Grab Pass compared to other passes using the Normal type. Otherwise use Normal.</para>
  /// </summary>
  GrabPass = 15, // 0x0000000F
}
```