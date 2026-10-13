using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace ShaderVariantCollectorTool
{
    /// <summary>
    /// Shader变体收集器窗口（支持中英文切换 / 配置持久化 / 结果统计面板）
    /// </summary>
    public class ShaderVariantCollectorWindow : EditorWindow
    {
        // ─────────────────────────── 枚举 ────────────────────────────
        private enum CollectionMode { Folder, Scene }

        private enum LogLevel { Info, Warning, Error }

        private struct LogEntry
        {
            public LogLevel Level;
            public string   Message;
        }

        // ─────────────────────────── 配置字段 ─────────────────────────
        private CollectionMode _collectionMode    = CollectionMode.Folder;
        private string  _materialFolderPath       = "Assets/";
        private string  _outputPath               = "Assets/ShaderVariants.shadervariants";
        private string  _sceneOutputFolder        = "Assets/ShaderVariants";
        private int     _batchSize                = 10;
        private bool    _exportJson               = true;
        private bool    _includePrefabs           = true;
        private bool    _isChinese                = true;

        // ─────────────────────────── 运行时状态 ────────────────────────
        private bool   _isCollecting;
        private float  _progress;
        private string _currentStatus = "";
        private System.Diagnostics.Stopwatch _collectionTimer;

        // 日志
        private readonly List<LogEntry> _logEntries    = new List<LogEntry>();
        private Vector2                 _logScrollPos;

        // 预览
        private List<string> _previewMaterials = new List<string>();
        private Vector2      _previewScrollPos;
        private bool         _showPreview;

        // 场景列表
        private List<string> _buildScenes = new List<string>();
        private Vector2      _sceneScrollPos;

        // 结果面板
        private bool   _hasResult;
        private int    _resultShaders;
        private int    _resultVariants;
        private float  _resultDuration;
        private string _resultOutputPath = "";

        // 折叠状态
        private bool _foldConfig  = true;
        private bool _foldPreview = true;
        private bool _foldLog     = true;

        // 核心收集器
        private ShaderVariantCollectorCore _collector;

        // ─────────────────────────── EditorPrefs 键 ───────────────────
        private const string PrefLang           = "SoraTools.SC.Lang";
        private const string PrefMode           = "SoraTools.SC.Mode";
        private const string PrefMatFolder      = "SoraTools.SC.MatFolder";
        private const string PrefOutputPath     = "SoraTools.SC.OutputPath";
        private const string PrefSceneFolder    = "SoraTools.SC.SceneFolder";
        private const string PrefBatchSize      = "SoraTools.SC.BatchSize";
        private const string PrefExportJson     = "SoraTools.SC.ExportJson";
        private const string PrefIncludePrefabs = "SoraTools.SC.IncludePrefabs";

        // ─────────────────────────── 本地化字典 ───────────────────────
        // 每条 [0]=中文  [1]=English
        private static readonly Dictionary<string, string[]> _loc =
            new Dictionary<string, string[]>
        {
            ["title"]             = new[] { "Shader 变体收集器",      "Shader Variant Collector"  },
            ["helpbox"]           = new[] {
                "收集材质 / 场景中的 Shader 变体，生成 ShaderVariantCollection 资产文件。",
                "Collect Shader variants from materials or scenes and generate a ShaderVariantCollection asset."
            },
            ["sec_config"]        = new[] { "配置",                   "Configuration"             },
            ["mode_label"]        = new[] { "收集模式",               "Collection Mode"           },
            ["mat_folder"]        = new[] { "材质文件夹",             "Material Folder"           },
            ["include_prefab"]    = new[] { "包含预制体材质",         "Include Prefab Materials"  },
            ["prefab_hint"]       = new[] {
                "同时扫描文件夹内预制体引用的材质（Renderer / ParticleSystem / UI）。",
                "Also scan materials referenced by prefabs (Renderer, ParticleSystem, UI, etc.)."
            },
            ["output_path"]       = new[] { "输出路径",               "Output Path"               },
            ["output_folder"]     = new[] { "输出文件夹",             "Output Folder"             },
            ["batch_size"]        = new[] { "批处理大小",             "Batch Size"                },
            ["batch_hint"]        = new[] {
                "值越小越安全，值越大越快速。推荐 10～20。",
                "Lower = safer, Higher = faster. Recommended: 10–20."
            },
            ["export_json"]       = new[] { "导出 JSON 清单",         "Export JSON Manifest"      },
            ["browse"]            = new[] { "浏览",                   "Browse"                    },
            ["open_dir"]          = new[] { "打开",                   "Open"                      },
            ["scene_list"]        = new[] { "Build Settings 场景列表","Scenes in Build Settings"  },
            ["refresh"]           = new[] { "刷新",                   "Refresh"                   },
            ["no_scenes"]         = new[] {
                "未找到已启用的场景，请检查 Build Settings。",
                "No enabled scenes found. Please check Build Settings."
            },
            ["scenes_count"]      = new[] { "已找到 {0} 个场景",     "{0} scenes found"          },
            ["btn_preview"]       = new[] { "预览材质",               "Preview Materials"         },
            ["btn_start"]         = new[] { "开始收集",               "Start Collection"          },
            ["btn_cancel"]        = new[] { "取消",                   "Cancel"                    },
            ["sec_result"]        = new[] { "收集结果",               "Collection Result"         },
            ["res_shaders"]       = new[] { "Shader 数",              "Shaders"                   },
            ["res_variants"]      = new[] { "变体数",                 "Variants"                  },
            ["res_duration"]      = new[] { "耗时",                   "Duration"                  },
            ["res_output"]        = new[] { "输出文件",               "Output"                    },
            ["res_reveal"]        = new[] { "在 Project 中定位",      "Reveal in Project"         },
            ["sec_preview"]       = new[] { "材质预览",               "Material Preview"          },
            ["sec_log"]           = new[] { "运行日志",               "Log"                       },
            ["clear_log"]         = new[] { "清空",                   "Clear"                     },
            ["confirm"]           = new[] { "确认",                   "Confirm"                   },
            ["folder_confirm"]    = new[] {
                "从文件夹收集 Shader 变体：\n{0}\n包含预制体材质：{1}\n\n输出至：\n{2}",
                "Collect Shader variants from folder:\n{0}\nInclude prefab materials: {1}\n\nOutput to:\n{2}"
            },
            ["scene_confirm"]     = new[] {
                "从 Build Settings 的 {0} 个场景收集 Shader 变体。\n\n输出文件夹：\n{1}",
                "Collect Shader variants from {0} scenes in Build Settings.\n\nOutput folder:\n{1}"
            },
            ["start"]             = new[] { "开始",                   "Start"                     },
            ["cancel"]            = new[] { "取消",                   "Cancel"                    },
            ["ok"]                = new[] { "确定",                   "OK"                        },
            ["success"]           = new[] { "成功",                   "Success"                   },
            ["failed"]            = new[] { "失败",                   "Failed"                    },
            ["cancelled"]         = new[] { "已取消",                 "Cancelled"                 },
            ["done"]              = new[] { "完成",                   "Done"                      },
            ["collecting"]        = new[] { "收集中...",              "Collecting..."             },
            ["ready"]             = new[] { "就绪",                   "Ready"                     },
            ["err_title"]         = new[] { "错误",                   "Error"                     },
            ["err_empty_folder"]  = new[] { "材质文件夹路径不能为空！","Material folder path is empty!" },
            ["err_empty_output"]  = new[] { "输出路径不能为空！",     "Output path is empty!"     },
            ["err_no_scenes"]     = new[] {
                "Build Settings 中未找到已启用的场景！",
                "No enabled scenes found in Build Settings!"
            },
            ["err_empty_scene_out"] = new[] { "输出文件夹路径不能为空！","Output folder path is empty!" },
            ["yes"]               = new[] { "是",                     "Yes"                       },
            ["no"]                = new[] { "否",                     "No"                        },
        };

        private string L(string key)
            => _loc.TryGetValue(key, out var arr) ? arr[_isChinese ? 0 : 1] : key;

        private string Lf(string key, params object[] args)
            => string.Format(L(key), args);

        // ─────────────────────────── 入口 ─────────────────────────────
        [MenuItem("SoraTools/Shader Variant Collector")]
        public static void ShowWindow()
        {
            var w = GetWindow<ShaderVariantCollectorWindow>();
            w.titleContent = new GUIContent("Shader Variant Collector");
            w.minSize      = new Vector2(480, 500);
        }

        private void OnEnable()
        {
            LoadPrefs();
            // domain reload 后 EditorStyles 实例会重建，必须清空缓存
            _logStyleInfo = _logStyleWarning = _logStyleError = null;
            _statBoxValueStyle = _statBoxLabelStyle = _resultTitleStyle = null;
            if (_buildScenes.Count == 0)
                RefreshBuildScenes();
        }

        private void OnDisable()
        {
            SavePrefs();
        }

        // ─────────────────────────── Prefs ────────────────────────────
        private void LoadPrefs()
        {
            _isChinese          = EditorPrefs.GetBool(PrefLang,          true);
            _collectionMode     = (CollectionMode)EditorPrefs.GetInt(PrefMode, 0);
            _materialFolderPath = EditorPrefs.GetString(PrefMatFolder,   "Assets/");
            _outputPath         = EditorPrefs.GetString(PrefOutputPath,  "Assets/ShaderVariants.shadervariants");
            _sceneOutputFolder  = EditorPrefs.GetString(PrefSceneFolder, "Assets/ShaderVariants");
            _batchSize          = EditorPrefs.GetInt(PrefBatchSize,      10);
            _exportJson         = EditorPrefs.GetBool(PrefExportJson,    true);
            _includePrefabs     = EditorPrefs.GetBool(PrefIncludePrefabs,true);
            if (string.IsNullOrEmpty(_currentStatus))
                _currentStatus = L("ready");
        }

        private void SavePrefs()
        {
            EditorPrefs.SetBool(PrefLang,           _isChinese);
            EditorPrefs.SetInt(PrefMode,            (int)_collectionMode);
            EditorPrefs.SetString(PrefMatFolder,    _materialFolderPath);
            EditorPrefs.SetString(PrefOutputPath,   _outputPath);
            EditorPrefs.SetString(PrefSceneFolder,  _sceneOutputFolder);
            EditorPrefs.SetInt(PrefBatchSize,       _batchSize);
            EditorPrefs.SetBool(PrefExportJson,     _exportJson);
            EditorPrefs.SetBool(PrefIncludePrefabs, _includePrefabs);
        }

        // ─────────────────────────── OnGUI 主布局 ─────────────────────
        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.Space(2);
            _foldConfig = DrawSection(L("sec_config"), _foldConfig, DrawConfiguration);

            EditorGUILayout.Space(4);
            DrawButtons();
            EditorGUILayout.Space(4);

            if (_isCollecting)
            {
                DrawProgressBar();
                EditorGUILayout.Space(4);
            }

            if (_hasResult && !_isCollecting)
            {
                DrawResultPanel();
                EditorGUILayout.Space(4);
            }

            if (_showPreview && _previewMaterials.Count > 0)
            {
                string previewTitle = $"{L("sec_preview")}  ({_previewMaterials.Count})";
                _foldPreview = DrawSection(previewTitle, _foldPreview, DrawPreview);
                EditorGUILayout.Space(2);
            }

            _foldLog = DrawSection(L("sec_log"), _foldLog, DrawLog);
        }

        // ─────────────────────────── 工具栏（含语言切换）──────────────
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(L("title"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            bool prevChinese = _isChinese;
            if (GUILayout.Toggle(_isChinese,  "中文", EditorStyles.toolbarButton, GUILayout.Width(38)))
                _isChinese = true;
            if (GUILayout.Toggle(!_isChinese, "EN",   EditorStyles.toolbarButton, GUILayout.Width(28)))
                _isChinese = false;
            if (_isChinese != prevChinese)
                SavePrefs();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(L("helpbox"), MessageType.Info);
        }

        // ─────────────────────────── 分组折叠辅助 ─────────────────────
        private bool DrawSection(string title, bool foldout, Action drawContent)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            foldout = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.foldoutHeader);
            if (foldout)
            {
                EditorGUILayout.Space(2);
                drawContent();
                EditorGUILayout.Space(2);
            }
            EditorGUILayout.EndVertical();
            return foldout;
        }

        // ─────────────────────────── 配置区域 ─────────────────────────
        private void DrawConfiguration()
        {
            EditorGUI.BeginChangeCheck();
            _collectionMode = (CollectionMode)EditorGUILayout.EnumPopup(L("mode_label"), _collectionMode);
            if (EditorGUI.EndChangeCheck()) SavePrefs();

            EditorGUILayout.Space(4);

            if (_collectionMode == CollectionMode.Folder)
                DrawFolderModeConfig();
            else
                DrawSceneModeConfig();

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _batchSize = EditorGUILayout.IntSlider(L("batch_size"), _batchSize, 1, 100);
            if (EditorGUI.EndChangeCheck()) SavePrefs();
            EditorGUILayout.LabelField(L("batch_hint"), EditorStyles.miniLabel);

            EditorGUILayout.Space(2);

            EditorGUI.BeginChangeCheck();
            _exportJson = EditorGUILayout.Toggle(L("export_json"), _exportJson);
            if (EditorGUI.EndChangeCheck()) SavePrefs();
        }

        private void DrawFolderModeConfig()
        {
            // 材质文件夹
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _materialFolderPath = EditorGUILayout.TextField(L("mat_folder"), _materialFolderPath);
            if (EditorGUI.EndChangeCheck()) SavePrefs();
            if (GUILayout.Button(L("browse"), GUILayout.Width(48)))
            {
                string p = EditorUtility.OpenFolderPanel("Select Material Folder", _materialFolderPath, "");
                if (!string.IsNullOrEmpty(p)) { _materialFolderPath = AbsoluteToAssetPath(p); SavePrefs(); }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            _includePrefabs = EditorGUILayout.Toggle(L("include_prefab"), _includePrefabs);
            if (EditorGUI.EndChangeCheck()) SavePrefs();
            EditorGUILayout.LabelField(L("prefab_hint"), EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            // 输出路径
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _outputPath = EditorGUILayout.TextField(L("output_path"), _outputPath);
            if (EditorGUI.EndChangeCheck()) SavePrefs();
            if (GUILayout.Button(L("browse"), GUILayout.Width(48)))
            {
                string p = EditorUtility.SaveFilePanel("Save Shader Variants", "Assets", "ShaderVariants", "shadervariants");
                if (!string.IsNullOrEmpty(p)) { _outputPath = AbsoluteToAssetPath(p); SavePrefs(); }
            }
            if (GUILayout.Button(L("open_dir"), GUILayout.Width(36)))
            {
                string dir = NormalizePath(Path.GetDirectoryName(_outputPath));
                if (!string.IsNullOrEmpty(dir))
                {
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dir);
                    if (obj != null) { EditorGUIUtility.PingObject(obj); Selection.activeObject = obj; }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSceneModeConfig()
        {
            // 输出文件夹
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _sceneOutputFolder = EditorGUILayout.TextField(L("output_folder"), _sceneOutputFolder);
            if (EditorGUI.EndChangeCheck()) SavePrefs();
            if (GUILayout.Button(L("browse"), GUILayout.Width(48)))
            {
                string p = EditorUtility.OpenFolderPanel("Select Output Folder", _sceneOutputFolder, "");
                if (!string.IsNullOrEmpty(p)) { _sceneOutputFolder = AbsoluteToAssetPath(p); SavePrefs(); }
            }
            if (GUILayout.Button(L("open_dir"), GUILayout.Width(36)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_sceneOutputFolder);
                if (obj != null) { EditorGUIUtility.PingObject(obj); Selection.activeObject = obj; }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // 场景列表
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(L("scene_list"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L("refresh"), GUILayout.Width(48)))
                RefreshBuildScenes();
            EditorGUILayout.EndHorizontal();

            if (_buildScenes.Count > 0)
            {
                EditorGUILayout.LabelField(Lf("scenes_count", _buildScenes.Count), EditorStyles.miniLabel);
                _sceneScrollPos = EditorGUILayout.BeginScrollView(_sceneScrollPos, GUILayout.Height(80));
                foreach (var s in _buildScenes)
                    EditorGUILayout.LabelField($"  • {Path.GetFileNameWithoutExtension(s)}", EditorStyles.miniLabel);
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.HelpBox(L("no_scenes"), MessageType.Warning);
            }
        }

        // ─────────────────────────── 按钮区 ───────────────────────────
        private void DrawButtons()
        {
            var prevBg = GUI.backgroundColor;
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !_isCollecting;

            if (GUILayout.Button(L("btn_preview"), GUILayout.Height(28)))
                PreviewMaterials();

            GUI.backgroundColor = _isCollecting ? Color.white : new Color(0.4f, 0.88f, 0.4f);
            if (GUILayout.Button(L("btn_start"), GUILayout.Height(28)))
                StartCollection();
            GUI.backgroundColor = prevBg;
            GUI.enabled = true;

            GUI.enabled = _isCollecting;
            GUI.backgroundColor = _isCollecting ? new Color(1f, 0.4f, 0.4f) : Color.white;
            if (GUILayout.Button(L("btn_cancel"), GUILayout.Height(28), GUILayout.Width(72)))
                CancelCollection();
            GUI.backgroundColor = prevBg;
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        // ─────────────────────────── 进度条 ───────────────────────────
        private void DrawProgressBar()
        {
            Rect r = EditorGUILayout.GetControlRect(false, 22);
            EditorGUI.ProgressBar(r, _progress, _currentStatus);
        }

        // ─────────────────────────── 结果面板 ─────────────────────────
        private void DrawResultPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 标题行
            if (_resultTitleStyle == null)
            {
                _resultTitleStyle = new GUIStyle(EditorStyles.boldLabel);
                _resultTitleStyle.normal.textColor = new Color(0.15f, 0.75f, 0.25f);
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"✓  {L("sec_result")}", _resultTitleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L("res_reveal"), GUILayout.Width(112)))
                PingResultAsset();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // 统计数据行
            EditorGUILayout.BeginHorizontal();
            DrawStatBox(L("res_shaders"),  _resultShaders.ToString());
            DrawStatBox(L("res_variants"), _resultVariants.ToString());
            DrawStatBox(L("res_duration"), $"{_resultDuration:F1}s");
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_resultOutputPath))
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField($"{L("res_output")}: {_resultOutputPath}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        // 缓存 StatBox / 结果面板标题样式
        private GUIStyle _statBoxValueStyle;
        private GUIStyle _statBoxLabelStyle;
        private GUIStyle _resultTitleStyle;

        private void DrawStatBox(string label, string value)
        {
            if (_statBoxValueStyle == null)
            {
                _statBoxValueStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
                _statBoxLabelStyle = new GUIStyle(EditorStyles.miniLabel)  { alignment = TextAnchor.MiddleCenter };
            }
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(80));
            GUILayout.Label(value, _statBoxValueStyle);
            GUILayout.Label(label, _statBoxLabelStyle);
            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────── 预览区 ───────────────────────────
        private void DrawPreview()
        {
            _previewScrollPos = EditorGUILayout.BeginScrollView(_previewScrollPos, GUILayout.Height(120));
            foreach (var m in _previewMaterials)
                EditorGUILayout.LabelField(m, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        // ─────────────────────────── 日志区（着色） ───────────────────
        private static readonly Color _colorError   = new Color(1f,  0.38f, 0.38f);
        private static readonly Color _colorWarning = new Color(1f,  0.82f, 0.26f);

        // 缓存日志样式，避免每帧每条目重复分配 GUIStyle
        private GUIStyle _logStyleInfo;
        private GUIStyle _logStyleWarning;
        private GUIStyle _logStyleError;

        private GUIStyle GetLogStyle(LogLevel level)
        {
            if (_logStyleInfo == null)
            {
                _logStyleInfo          = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                _logStyleWarning       = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                _logStyleWarning.normal.textColor = _colorWarning;
                _logStyleError         = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                _logStyleError.normal.textColor   = _colorError;
            }
            return level == LogLevel.Error   ? _logStyleError
                 : level == LogLevel.Warning ? _logStyleWarning
                 : _logStyleInfo;
        }

        private void DrawLog()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L("clear_log"), GUILayout.Width(48)))
                _logEntries.Clear();
            EditorGUILayout.EndHorizontal();

            _logScrollPos = EditorGUILayout.BeginScrollView(_logScrollPos, GUILayout.Height(180));
            foreach (var entry in _logEntries)
                GUILayout.Label(entry.Message, GetLogStyle(entry.Level));
            EditorGUILayout.EndScrollView();
        }

        // ─────────────────────────── 预览材质 ─────────────────────────
        private void PreviewMaterials()
        {
            _previewMaterials.Clear();
            _showPreview = false;

            if (_collectionMode == CollectionMode.Folder)
            {
                if (string.IsNullOrEmpty(_materialFolderPath))
                {
                    AddLog(L("err_empty_folder"), LogLevel.Error);
                    return;
                }

                var paths = new HashSet<string>();
                try
                {
                    EditorUtility.DisplayProgressBar(L("btn_preview"), L("collecting"), 0f);
                    string[] folders = { _materialFolderPath.TrimEnd('/', '\\') };
                    string[] guids   = AssetDatabase.FindAssets("t:Material", folders);
                    for (int i = 0; i < guids.Length; i++)
                    {
                        if (i % 50 == 0)
                            EditorUtility.DisplayProgressBar(L("btn_preview"),
                                $"{i}/{guids.Length}", (float)i / guids.Length * 0.5f);
                        string p = AssetDatabase.GUIDToAssetPath(guids[i]);
                        if (!string.IsNullOrEmpty(p)) paths.Add(p);
                    }
                    AddLog($"[Preview] .mat: {paths.Count}");

                    if (_includePrefabs)
                    {
                        EditorUtility.DisplayProgressBar(L("btn_preview"), "Prefabs...", 0.5f);
                        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", folders);
                        int added = 0;
                        for (int i = 0; i < prefabGuids.Length; i++)
                        {
                            if (i % 10 == 0)
                                EditorUtility.DisplayProgressBar(L("btn_preview"),
                                    $"Prefab {i}/{prefabGuids.Length}",
                                    0.5f + (float)i / prefabGuids.Length * 0.5f);
                            string pp     = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                            var prefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(pp);
                            if (prefabObj != null)
                            {
                                var mats = new HashSet<string>();
                                ShaderVariantCollectorCore.CollectMaterialsFromGameObject(prefabObj, mats, true);
                                foreach (var m in mats) { if (paths.Add(m)) added++; }
                            }
                            if (i % 100 == 0 && i > 0) Resources.UnloadUnusedAssets();
                        }
                        AddLog($"[Preview] +{added} from {prefabGuids.Length} prefabs");
                    }

                    _previewMaterials = new List<string>(paths);
                    _showPreview      = true;
                    AddLog($"[Preview] Total: {_previewMaterials.Count}");
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                    Resources.UnloadUnusedAssets();
                }
            }
            else
            {
                // 场景模式
                RefreshBuildScenes();
                if (_buildScenes.Count == 0)
                {
                    AddLog(L("err_no_scenes"), LogLevel.Warning);
                    return;
                }

                try
                {
                    var all = new HashSet<string>();
                    for (int i = 0; i < _buildScenes.Count; i++)
                    {
                        string sn = Path.GetFileNameWithoutExtension(_buildScenes[i]);
                        EditorUtility.DisplayProgressBar(L("btn_preview"),
                            $"{i+1}/{_buildScenes.Count}: {sn}", (float)i / _buildScenes.Count);
                        var mats = ShaderVariantCollectorCore.ScanMaterialsInScene(_buildScenes[i]);
                        foreach (var m in mats) all.Add(m);
                        AddLog($"[Preview] {sn}: {mats.Count}");
                    }
                    _previewMaterials = new List<string>(all);
                    _showPreview      = true;
                    AddLog($"[Preview] Total: {_previewMaterials.Count} ({_buildScenes.Count} scenes)");
                }
                catch (Exception e)
                {
                    AddLog($"[Preview] Error: {e.Message}", LogLevel.Error);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                    Resources.UnloadUnusedAssets();
                }
            }
        }

        // ─────────────────────────── 开始收集 ─────────────────────────
        private void StartCollection()
        {
            if (_isCollecting) return;
            if (_collectionMode == CollectionMode.Folder)
                StartFolderModeCollection();
            else
                StartSceneModeCollection();
        }

        private void StartFolderModeCollection()
        {
            if (string.IsNullOrEmpty(_materialFolderPath))
            { EditorUtility.DisplayDialog(L("err_title"), L("err_empty_folder"), L("ok")); return; }
            if (string.IsNullOrEmpty(_outputPath))
            { EditorUtility.DisplayDialog(L("err_title"), L("err_empty_output"), L("ok")); return; }

            string prefabInfo = _includePrefabs ? L("yes") : L("no");
            if (!EditorUtility.DisplayDialog(L("confirm"),
                Lf("folder_confirm", _materialFolderPath, prefabInfo, _outputPath),
                L("start"), L("cancel")))
                return;

            _logEntries.Clear();
            _progress        = 0f;
            _currentStatus   = L("collecting");
            _isCollecting    = true;
            _hasResult       = false;
            _collectionTimer = System.Diagnostics.Stopwatch.StartNew();

            AddLog("=== Collection Start (Folder) ===");
            AddLog($"Folder: {_materialFolderPath}");
            AddLog($"Output: {_outputPath}");
            AddLog($"BatchSize: {_batchSize} | IncludePrefabs: {_includePrefabs}");

            _collector = new ShaderVariantCollectorCore();
            _collector.StartCollection(_materialFolderPath, _outputPath, _batchSize, _exportJson,
                _includePrefabs, OnProgressUpdate, OnCollectionComplete);
        }

        private void StartSceneModeCollection()
        {
            RefreshBuildScenes();
            if (_buildScenes.Count == 0)
            { EditorUtility.DisplayDialog(L("err_title"), L("err_no_scenes"), L("ok")); return; }
            if (string.IsNullOrEmpty(_sceneOutputFolder))
            { EditorUtility.DisplayDialog(L("err_title"), L("err_empty_scene_out"), L("ok")); return; }

            if (!EditorUtility.DisplayDialog(L("confirm"),
                Lf("scene_confirm", _buildScenes.Count, _sceneOutputFolder),
                L("start"), L("cancel")))
                return;

            _logEntries.Clear();
            _progress        = 0f;
            _currentStatus   = L("collecting");
            _isCollecting    = true;
            _hasResult       = false;
            _collectionTimer = System.Diagnostics.Stopwatch.StartNew();

            AddLog("=== Collection Start (Scene) ===");
            AddLog($"Scenes: {_buildScenes.Count}");
            AddLog($"Output: {_sceneOutputFolder}");
            AddLog($"BatchSize: {_batchSize}");

            _collector = new ShaderVariantCollectorCore();
            _collector.StartCollectionByScenes(_buildScenes, _sceneOutputFolder, _batchSize,
                _exportJson, OnProgressUpdate, OnCollectionComplete);
        }

        // ─────────────────────────── 取消收集 ─────────────────────────
        private void CancelCollection()
        {
            if (!_isCollecting || _collector == null) return;
            _collector.CancelCollection();
            _collectionTimer?.Stop();
            _isCollecting  = false;
            _progress      = 0f;
            _currentStatus = L("cancelled");
            AddLog($"=== {L("cancelled")} ===", LogLevel.Warning);
        }

        // ─────────────────────────── 回调 ─────────────────────────────
        private void OnProgressUpdate(float progress, string message)
        {
            _progress      = progress;
            _currentStatus = message;
            AddLog(message);
            Repaint();
        }

        private void OnCollectionComplete(bool success, string message)
        {
            _isCollecting = false;
            if (_collectionTimer != null)
            {
                _collectionTimer.Stop();
                _resultDuration = (float)_collectionTimer.Elapsed.TotalSeconds;
            }
            _progress      = success ? 1f : 0f;
            _currentStatus = success ? L("done") : L("failed");

            if (success)
            {
                _hasResult        = true;
                _resultOutputPath = _collectionMode == CollectionMode.Folder
                    ? _outputPath : _sceneOutputFolder;
                ParseResultCounts(message);

                AddLog($"=== {L("success")} ===");
                AddLog(message);
                EditorUtility.DisplayDialog(L("success"), message, L("ok"));
                PingResultAsset();
            }
            else
            {
                AddLog($"=== {L("failed")} ===", LogLevel.Error);
                AddLog(message, LogLevel.Error);
                EditorUtility.DisplayDialog(L("failed"), message, L("ok"));
            }
            Repaint();
        }

        // ─────────────────────────── 辅助方法 ─────────────────────────
        private void ParseResultCounts(string message)
        {
            _resultShaders  = 0;
            _resultVariants = 0;
            var sm = Regex.Match(message, @"Shaders?[:\s]+(\d+)", RegexOptions.IgnoreCase);
            var vm = Regex.Match(message, @"Variants?[:\s]+(\d+)", RegexOptions.IgnoreCase);
            if (sm.Success) int.TryParse(sm.Groups[1].Value, out _resultShaders);
            if (vm.Success) int.TryParse(vm.Groups[1].Value, out _resultVariants);
        }

        private void PingResultAsset()
        {
            // 优先定位具体文件
            string targetPath = _collectionMode == CollectionMode.Folder
                ? _outputPath
                : _sceneOutputFolder + "/_Master.shadervariants";

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath);
            if (asset != null) { EditorGUIUtility.PingObject(asset); Selection.activeObject = asset; return; }

            // 回退到文件夹
            string dir = _collectionMode == CollectionMode.Folder
                ? NormalizePath(Path.GetDirectoryName(_outputPath))
                : _sceneOutputFolder;
            if (!string.IsNullOrEmpty(dir))
            {
                var folderObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dir);
                if (folderObj != null) { EditorGUIUtility.PingObject(folderObj); Selection.activeObject = folderObj; }
            }
        }

        private void AddLog(string message, LogLevel level = LogLevel.Info)
        {
            // 自动检测日志级别
            if (level == LogLevel.Info)
            {
                string lc = message.ToLowerInvariant();
                if (lc.Contains("error") || lc.Contains("failed") || lc.Contains("失败") || lc.Contains("错误"))
                    level = LogLevel.Error;
                else if (lc.Contains("warning") || lc.Contains("warn") || lc.Contains("警告") || lc.Contains("skipping"))
                    level = LogLevel.Warning;
            }
            string ts = DateTime.Now.ToString("HH:mm:ss");
            _logEntries.Add(new LogEntry { Level = level, Message = $"[{ts}] {message}" });
            if (_logEntries.Count > 500)
                _logEntries.RemoveAt(0);
            _logScrollPos.y = float.MaxValue; // 自动滚动到最新日志
        }

        private void RefreshBuildScenes()
        {
            _buildScenes = ShaderVariantCollectorCore.GetScenesInBuildSettings();
            AddLog($"Build Scenes: {_buildScenes.Count}");
        }

        private string AbsoluteToAssetPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return absolutePath;
            string normalized   = NormalizePath(absolutePath);
            string dataPath     = NormalizePath(Application.dataPath);
            string projectPath  = NormalizePath(Path.GetDirectoryName(dataPath));
            if (string.IsNullOrEmpty(projectPath)) return absolutePath;
            if (normalized.StartsWith(projectPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                string rel = normalized.Substring(projectPath.Length + 1);
                if (rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return rel;
            }
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return normalized;
            return absolutePath;
        }

        private static string NormalizePath(string path)
            => string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
    }
}
