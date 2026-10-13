using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using UnityEditor;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Tools
{
    /// <summary>
    /// Controller for the Tools section inside the MCP For Unity editor window.
    /// Provides discovery, filtering, and per-tool enablement toggles.
    /// </summary>
    public class McpToolsSection
    {
        private static readonly Dictionary<string, ToolGuide> ToolGuides = new(StringComparer.OrdinalIgnoreCase)
        {
            ["batch_execute"] = new ToolGuide(
                "批量执行",
                "一次提交多条 UnityMCP 命令，减少 Codex 和 Unity 之间的多次往返。",
                "批量创建对象、连续修改多个组件、执行重复性编辑器操作。",
                "Unity 侧仍会按命令顺序执行，不等同于真正并行。"),
            ["execute_menu_item"] = new ToolGuide(
                "执行菜单项",
                "调用 Unity Editor 顶部菜单里的命令。",
                "打开编辑器窗口、触发现有菜单功能、执行项目已有工具入口。",
                "部分菜单依赖当前选择或编辑器状态，菜单不可用时会失败。"),
            ["find_gameobjects"] = new ToolGuide(
                "查找场景对象",
                "按名称、标签、层级、组件或实例 ID 查找当前场景里的 GameObject。",
                "先定位对象，再继续修改组件、材质、Transform 或层级关系。",
                string.Empty),
            ["get_test_job"] = new ToolGuide(
                "查询测试任务",
                "查询 run_tests 启动的 Unity Test Runner 异步任务状态。",
                "轮询测试是否完成，查看失败、跳过和通过数量。",
                "必须配合 run_tests 返回的 job_id 使用。"),
            ["inspect_references"] = new ToolGuide(
                "引用关系检查",
                "只读检查资源 GUID 反向引用、场景 Missing Script 和 Missing Reference。",
                "定位某个 Prefab、材质、脚本被哪些资源引用，或让 Codex 快速判断当前场景是否有断引用。",
                "资源反向引用会扫描文本资源，项目很大时建议限制 search_root 和 limit。"),
            ["inspect_runtime"] = new ToolGuide(
                "运行时检查",
                "只读读取当前编辑器和场景运行状态，包括对象、组件、相机、动画、Canvas 和选择集。",
                "PlayMode 调试、检查目标对象组件绑定、确认相机/UI/Animator 是否按预期存在。",
                "include_fields 会读取序列化字段摘要，复杂对象较多时建议限制 target 或 limit。"),
            ["manage_asset"] = new ToolGuide(
                "管理资源",
                "搜索、创建、导入、移动、重命名、删除或查看 Unity 资源。",
                "查找脚本、材质、Prefab、场景文件，创建文件夹或读取资源信息。",
                "删除、移动、重命名会真实改动 Assets 或 Packages 下的资源。"),
            ["manage_components"] = new ToolGuide(
                "管理组件",
                "给 GameObject 添加、移除组件，或设置组件属性。",
                "添加 Rigidbody、修改脚本字段、调整 Collider、Renderer 等组件参数。",
                "会修改当前场景或 Prefab 实例，可能让场景变为未保存状态。"),
            ["manage_editor"] = new ToolGuide(
                "控制编辑器",
                "查询或控制 Unity Editor 状态。",
                "进入或退出 PlayMode、暂停、管理 Tag/Layer、查看遥测状态。",
                "PlayMode 和 Tag/Layer 操作会影响当前编辑器会话。"),
            ["manage_gameobject"] = new ToolGuide(
                "管理场景对象",
                "创建、修改、删除、复制或移动 GameObject。",
                "搭建测试对象、调整 Transform、修改名称/Tag/Layer、清理临时对象。",
                "会修改当前场景；删除和移动前应确认目标对象。"),
            ["manage_material"] = new ToolGuide(
                "管理材质",
                "创建材质、设置颜色或 Shader 属性，并可绑定到 Renderer。",
                "快速换材质、调试渲染效果、设置 MaterialPropertyBlock。",
                "直接改 shared material 时会影响复用同一材质的对象。"),
            ["manage_prefabs"] = new ToolGuide(
                "管理 Prefab",
                "创建 Prefab，或打开、保存、关闭 Prefab Stage。",
                "从场景对象生成 Prefab，隔离编辑 Prefab 资源。",
                "保存 Prefab Stage 会写入 Prefab 资产。"),
            ["manage_scene"] = new ToolGuide(
                "管理场景",
                "查询当前场景、Hierarchy、Build Settings，也可创建、加载、保存场景和截图。",
                "确认当前场景、读取层级结构、保存场景、生成编辑器截图。",
                "加载或保存场景属于高影响操作，可能丢失未保存修改。"),
            ["manage_script"] = new ToolGuide(
                "管理脚本",
                "读取、创建或删除 C# 脚本。",
                "查看源码、生成组件脚本、清理不再需要的脚本文件。",
                "创建或删除脚本后通常会触发 Unity 重新编译。"),
            ["manage_scriptable_object"] = new ToolGuide(
                "管理 ScriptableObject",
                "创建或修改 ScriptableObject 资产的序列化字段。",
                "维护配置资产、表驱动数据、关卡或功能参数。",
                "会写入 .asset 文件，修改前应确认目标资源路径。"),
            ["manage_shader"] = new ToolGuide(
                "管理 Shader",
                "读取、创建、更新或删除 Shader 文件。",
                "检查 Shader 源码、生成测试 Shader、快速修改渲染逻辑。",
                "修改后可能触发资源导入或 Shader 编译。"),
            ["manage_vfx"] = new ToolGuide(
                "管理特效",
                "控制 ParticleSystem、LineRenderer、TrailRenderer 和 VFX Graph。",
                "读取粒子信息、播放或停止特效、调整线条、轨迹和 VFX 参数。",
                "VFX Graph 相关操作依赖项目安装对应包和资产。"),
            ["project_health_check"] = new ToolGuide(
                "项目体检",
                "生成结构化项目健康报告，覆盖当前场景、Build Settings、asmdef、UnityMCP 连接和可选资源扫描。",
                "每次 VibeCoding 前后快速确认项目是否处于可操作状态，给 Codex 提供机器可读诊断结果。",
                "full 或 deep_scan_assets 会扫描资源文件，项目很大时可能耗时。"),
            ["read_console"] = new ToolGuide(
                "读取控制台",
                "读取或清空 Unity Console 日志。",
                "检查编译错误、运行时报错、警告和最近日志。",
                "日志量很大时建议限制 count 或使用过滤条件。"),
            ["refresh_unity"] = new ToolGuide(
                "刷新 Unity",
                "刷新 AssetDatabase，并可请求脚本编译和等待编辑器就绪。",
                "修改脚本或资源后触发导入、编译，再检查 Console。",
                "等待编译时可能耗时，取决于项目规模。"),
            ["run_tests"] = new ToolGuide(
                "运行测试",
                "启动 Unity Test Runner 的 EditMode 或 PlayMode 测试任务。",
                "验证脚本改动、回归测试、配合 get_test_job 查询结果。",
                "测试可能耗时，且同一编辑器里测试任务数量有限。")
        };

        private readonly Dictionary<string, Toggle> toolToggleMap = new();
        private Label summaryLabel;
        private Label noteLabel;
        private TextField searchField;
        private Button enableAllButton;
        private Button disableAllButton;
        private Button rescanButton;
        private VisualElement categoryContainer;
        private List<ToolMetadata> allTools = new();
        private string searchQuery = string.Empty;

        public VisualElement Root { get; }

        public McpToolsSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            summaryLabel = Root.Q<Label>("tools-summary");
            noteLabel = Root.Q<Label>("tools-note");
            searchField = Root.Q<TextField>("tool-search");
            enableAllButton = Root.Q<Button>("enable-all-button");
            disableAllButton = Root.Q<Button>("disable-all-button");
            rescanButton = Root.Q<Button>("rescan-button");
            categoryContainer = Root.Q<VisualElement>("tool-category-container");
        }

        private void RegisterCallbacks()
        {
            if (enableAllButton != null)
            {
                enableAllButton.AddToClassList("tool-action-button");
                enableAllButton.style.marginRight = 4;
                enableAllButton.clicked += () => SetAllToolsState(true);
            }

            if (disableAllButton != null)
            {
                disableAllButton.AddToClassList("tool-action-button");
                disableAllButton.style.marginRight = 4;
                disableAllButton.clicked += () => SetAllToolsState(false);
            }

            if (rescanButton != null)
            {
                rescanButton.AddToClassList("tool-action-button");
                rescanButton.clicked += () =>
                {
                    McpLog.Info("Rescanning MCP tools from the editor window.");
                    MCPServiceLocator.ToolDiscovery.InvalidateCache();
                    Refresh();
                };
            }

            if (searchField != null)
            {
                searchField.tooltip = "按中文名称、英文工具名、用途、场景或参数搜索。";
                searchField.RegisterValueChangedCallback(evt =>
                {
                    searchQuery = evt.newValue?.Trim() ?? string.Empty;
                    RebuildToolList();
                });
            }
        }

        /// <summary>
        /// Rebuilds the tool list and synchronises toggle states.
        /// </summary>
        public void Refresh()
        {
            toolToggleMap.Clear();

            var service = MCPServiceLocator.ToolDiscovery;
            allTools = service.DiscoverAllTools()
                .OrderBy(tool => IsBuiltIn(tool) ? 0 : 1)
                .ThenBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool hasTools = allTools.Count > 0;
            enableAllButton?.SetEnabled(hasTools);
            disableAllButton?.SetEnabled(hasTools);

            if (noteLabel != null)
            {
                noteLabel.style.display = hasTools ? DisplayStyle.Flex : DisplayStyle.None;
            }

            RebuildToolList();
        }

        private void RebuildToolList()
        {
            toolToggleMap.Clear();
            categoryContainer?.Clear();

            bool hasTools = allTools.Count > 0;
            if (!hasTools)
            {
                AddInfoLabel("没有发现 MCP 工具。给工具类添加 [McpForUnityTool] 后会显示在这里。");
                UpdateSummary(0);
                return;
            }

            var visibleTools = GetFilteredTools().ToList();
            if (visibleTools.Count == 0)
            {
                AddInfoLabel($"没有找到匹配“{searchQuery}”的工具。");
                UpdateSummary(0);
                return;
            }

            BuildCategory("内置工具", "built-in", visibleTools.Where(IsBuiltIn));

            var customTools = visibleTools.Where(tool => !IsBuiltIn(tool)).ToList();
            if (customTools.Count > 0)
            {
                BuildCategory("自定义工具", "custom", customTools);
            }
            else if (string.IsNullOrEmpty(searchQuery))
            {
                AddInfoLabel("当前没有发现项目自定义工具。");
            }

            UpdateSummary(visibleTools.Count);
        }

        private IEnumerable<ToolMetadata> GetFilteredTools()
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                return allTools;
            }

            return allTools.Where(MatchesSearch);
        }

        private bool MatchesSearch(ToolMetadata tool)
        {
            var guide = GetToolGuide(tool);
            return ContainsSearch(tool?.Name) ||
                   ContainsSearch(tool?.Description) ||
                   ContainsSearch(guide.DisplayName) ||
                   ContainsSearch(guide.Summary) ||
                   ContainsSearch(guide.Scenario) ||
                   ContainsSearch(guide.Risk) ||
                   (tool?.Parameters != null && tool.Parameters.Any(parameter =>
                       ContainsSearch(parameter.Name) ||
                       ContainsSearch(parameter.Description) ||
                       ContainsSearch(parameter.Type)));
        }

        private bool ContainsSearch(string text)
        {
            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateVisibleSummary()
        {
            UpdateSummary(GetFilteredTools().Count());
        }

        private void UpdateSummary()
        {
            UpdateVisibleSummary();
        }

        private void UpdateSummary(int visibleCount)
        {
            if (summaryLabel == null)
            {
                return;
            }

            if (allTools.Count == 0)
            {
                summaryLabel.text = "没有发现 MCP 工具。";
                return;
            }

            int enabledCount = allTools.Count(tool => MCPServiceLocator.ToolDiscovery.IsToolEnabled(tool.Name));
            string filterText = string.IsNullOrEmpty(searchQuery)
                ? string.Empty
                : $"，当前筛选显示 {visibleCount} 个";
            summaryLabel.text = $"已启用 {enabledCount} / 共 {allTools.Count} 个工具{filterText}，会注册给已连接的客户端。";
        }

        private void UpdateSummaryAfterStateChange()
        {
            UpdateVisibleSummary();
        }

        private void BuildCategory(string title, string prefsSuffix, IEnumerable<ToolMetadata> tools)
        {
            var toolList = tools.ToList();
            if (toolList.Count == 0)
            {
                return;
            }

            var foldout = new Foldout
            {
                text = $"{title} ({toolList.Count})",
                value = EditorPrefs.GetBool(EditorPrefKeys.ToolFoldoutStatePrefix + prefsSuffix, true)
            };

            foldout.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool(EditorPrefKeys.ToolFoldoutStatePrefix + prefsSuffix, evt.newValue);
            });

            foreach (var tool in toolList)
            {
                foldout.Add(CreateToolRow(tool));
            }

            categoryContainer?.Add(foldout);
        }

        private VisualElement CreateToolRow(ToolMetadata tool)
        {
            var guide = GetToolGuide(tool);

            var row = new VisualElement();
            row.AddToClassList("tool-item");

            var header = new VisualElement();
            header.AddToClassList("tool-item-header");

            var toggle = new Toggle(guide.DisplayName)
            {
                value = MCPServiceLocator.ToolDiscovery.IsToolEnabled(tool.Name)
            };
            toggle.AddToClassList("tool-item-toggle");
            toggle.tooltip = BuildToolTooltip(tool, guide);

            toggle.RegisterValueChangedCallback(evt =>
            {
                HandleToggleChange(tool, evt.newValue);
            });

            toolToggleMap[tool.Name] = toggle;
            header.Add(toggle);

            var tagsContainer = new VisualElement();
            tagsContainer.AddToClassList("tool-tags");

            bool defaultEnabled = tool.AutoRegister || tool.IsBuiltIn;
            tagsContainer.Add(CreateTag(defaultEnabled ? "默认启用" : "默认关闭"));

            tagsContainer.Add(CreateTag(tool.StructuredOutput ? "结构化输出" : "自由文本"));

            if (tool.RequiresPolling)
            {
                tagsContainer.Add(CreateTag($"轮询: {tool.PollAction}"));
            }

            header.Add(tagsContainer);
            row.Add(header);

            var rawName = new Label($"英文名: {tool.Name}");
            rawName.AddToClassList("tool-raw-name");
            row.Add(rawName);

            row.Add(CreateGuideLabel("用途", guide.Summary, "tool-item-description"));
            row.Add(CreateGuideLabel("场景", guide.Scenario, "tool-item-description"));

            if (!string.IsNullOrWhiteSpace(guide.Risk))
            {
                row.Add(CreateGuideLabel("注意", guide.Risk, "tool-item-risk"));
            }

            if (!IsDefaultDescription(tool) &&
                !string.IsNullOrWhiteSpace(tool.Description) &&
                !string.Equals(tool.Description, guide.Summary, StringComparison.OrdinalIgnoreCase))
            {
                row.Add(CreateGuideLabel("原始说明", tool.Description, "tool-item-description"));
            }

            if (tool.Parameters != null && tool.Parameters.Count > 0)
            {
                var paramSummary = string.Join(", ", tool.Parameters.Select(p =>
                    $"{p.Name}{(p.Required ? string.Empty : " (可选)")}: {p.Type}"));

                var parametersLabel = new Label($"参数: {paramSummary}");
                parametersLabel.AddToClassList("tool-parameters");
                row.Add(parametersLabel);
            }

            if (IsManageSceneTool(tool))
            {
                row.Add(CreateManageSceneActions(tool, toggle));
            }

            return row;
        }

        private void HandleToggleChange(ToolMetadata tool, bool enabled, bool updateSummary = true)
        {
            MCPServiceLocator.ToolDiscovery.SetToolEnabled(tool.Name, enabled);

            if (updateSummary)
            {
                UpdateSummaryAfterStateChange();
            }
        }

        private void SetAllToolsState(bool enabled)
        {
            foreach (var tool in allTools)
            {
                if (!toolToggleMap.TryGetValue(tool.Name, out var toggle))
                {
                    MCPServiceLocator.ToolDiscovery.SetToolEnabled(tool.Name, enabled);
                    continue;
                }

                if (toggle.value == enabled)
                {
                    continue;
                }

                toggle.SetValueWithoutNotify(enabled);
                HandleToggleChange(tool, enabled, updateSummary: false);
            }

            UpdateSummaryAfterStateChange();
        }

        private void AddInfoLabel(string message)
        {
            var label = new Label(message);
            label.AddToClassList("help-text");
            categoryContainer?.Add(label);
        }

        private VisualElement CreateManageSceneActions(ToolMetadata tool, Toggle toolToggle)
        {
            var actions = new VisualElement();
            actions.AddToClassList("tool-item-actions");

            var screenshotButton = new Button(OnManageSceneScreenshotClicked)
            {
                text = "截取场景截图"
            };
            screenshotButton.AddToClassList("tool-action-button");
            screenshotButton.style.marginTop = 4;
            screenshotButton.tooltip = "通过 manage_scene 将当前编辑器画面截图保存到 Assets/Screenshots。";
            screenshotButton.SetEnabled(MCPServiceLocator.ToolDiscovery.IsToolEnabled(tool.Name));
            toolToggle?.RegisterValueChangedCallback(evt => screenshotButton.SetEnabled(evt.newValue));

            actions.Add(screenshotButton);
            return actions;
        }

        private void OnManageSceneScreenshotClicked()
        {
            try
            {
                var response = ManageScene.ExecuteScreenshot();
                if (response is SuccessResponse success && !string.IsNullOrWhiteSpace(success.Message))
                {
                    McpLog.Info(success.Message);
                }
                else if (response is ErrorResponse error && !string.IsNullOrWhiteSpace(error.Error))
                {
                    McpLog.Error(error.Error);
                }
                else
                {
                    McpLog.Info("已请求截取场景截图。");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"截取场景截图失败: {ex.Message}");
            }
        }

        private static Label CreateGuideLabel(string title, string text, string className)
        {
            var label = new Label($"{title}: {text}");
            label.AddToClassList(className);
            return label;
        }

        private static Label CreateTag(string text)
        {
            var tag = new Label(text);
            tag.AddToClassList("tool-tag");
            return tag;
        }

        private static ToolGuide GetToolGuide(ToolMetadata tool)
        {
            if (tool != null && ToolGuides.TryGetValue(tool.Name, out var guide))
            {
                return guide;
            }

            string displayName = string.IsNullOrWhiteSpace(tool?.Name) ? "未命名工具" : tool.Name;
            string summary = IsDefaultDescription(tool)
                ? "项目自定义 MCP 工具，具体用途取决于脚本实现。"
                : tool.Description;

            return new ToolGuide(
                displayName,
                summary,
                "查看脚本说明和参数后决定是否启用或调用。",
                "自定义工具可能会修改项目数据，使用前应确认实现逻辑。");
        }

        private static string BuildToolTooltip(ToolMetadata tool, ToolGuide guide)
        {
            return $"{guide.DisplayName}\n{tool.Name}\n用途: {guide.Summary}";
        }

        private static bool IsDefaultDescription(ToolMetadata tool)
        {
            return tool == null ||
                   string.IsNullOrWhiteSpace(tool.Description) ||
                   string.Equals(tool.Description, $"Tool: {tool.Name}", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsManageSceneTool(ToolMetadata tool) => string.Equals(tool?.Name, "manage_scene", StringComparison.OrdinalIgnoreCase);

        private static bool IsBuiltIn(ToolMetadata tool) => tool?.IsBuiltIn ?? false;

        private sealed class ToolGuide
        {
            public ToolGuide(string displayName, string summary, string scenario, string risk)
            {
                DisplayName = displayName;
                Summary = summary;
                Scenario = scenario;
                Risk = risk;
            }

            public string DisplayName { get; }
            public string Summary { get; }
            public string Scenario { get; }
            public string Risk { get; }
        }
    }
}
