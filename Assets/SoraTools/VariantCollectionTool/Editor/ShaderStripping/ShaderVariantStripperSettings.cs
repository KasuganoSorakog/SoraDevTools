using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SoraTools
{
    /// <summary>
    /// Shader变体剥离器配置文件（ScriptableObject）
    /// 通过 Edit → Project Settings → Sora Tools → Shader Variant Stripper 进行管理
    /// </summary>
    public class ShaderVariantStripperSettings : ScriptableObject
    {
        // ── 配置文件的固定存储路径 ──────────────────────────────────────
        private const string AssetPath =
            "Assets/SoraTools/VariantCollectionTool/Editor/ShaderStripping/ShaderVariantStripperSettings.asset";

        // ── 字段 ────────────────────────────────────────────────────────
        [Tooltip("是否在打包时启用 Shader 变体剥离。禁用后所有变体均会保留。")]
        public bool isEnabled = true;

        [Tooltip("打包时需要剥离的 Shader 关键字列表。包含这些关键字的变体将从 Build 中移除。")]
        public List<string> keywordsToStrip = new List<string>(DefaultKeywords);

        // ── 获取或自动创建配置文件 ──────────────────────────────────────
        public static ShaderVariantStripperSettings GetOrCreate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ShaderVariantStripperSettings>(AssetPath);
            if (settings != null)
                return settings;

            // 确保目录存在
            string dir = Path.GetDirectoryName(AssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                // 逐级创建（通常目录已存在）
                string[] parts = dir.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }

            settings = CreateInstance<ShaderVariantStripperSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ShaderVariantStripper] Created settings asset at: {AssetPath}");
            return settings;
        }

        // ── Project Settings 面板注册 ───────────────────────────────────
        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/Sora Tools/Shader Variant Stripper", SettingsScope.Project)
            {
                label = "Shader Variant Stripper",
                guiHandler = _ => DrawSettingsGUI(),
                keywords = new HashSet<string> { "shader", "variant", "strip", "keyword", "build" }
            };
        }

        // ── 面板绘制 ────────────────────────────────────────────────────
        private static SerializedObject  _serializedSettings;
        private static SerializedProperty _propEnabled;
        private static SerializedProperty _propKeywords;
        private static Vector2 _scrollPos;

        private static void DrawSettingsGUI()
        {
            var settings = GetOrCreate();

            // 重新绑定（首次或资源重载后）
            if (_serializedSettings == null || _serializedSettings.targetObject != settings)
            {
                _serializedSettings = new SerializedObject(settings);
                _propEnabled  = _serializedSettings.FindProperty("isEnabled");
                _propKeywords = _serializedSettings.FindProperty("keywordsToStrip");
            }

            _serializedSettings.Update();

            // ── 启用开关 ──────────────────────────────────────────────
            EditorGUILayout.Space(4);
            var prevBg = GUI.backgroundColor;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("打包时启用变体剥离", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            bool enabled = _propEnabled.boolValue;
            GUI.backgroundColor = enabled ? new Color(0.4f, 0.9f, 0.4f) : new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Toggle(enabled, enabled ? "  已启用  " : "  已禁用  ",
                GUI.skin.button, GUILayout.Width(90), GUILayout.Height(22)))
                _propEnabled.boolValue = true;
            else
                _propEnabled.boolValue = false;
            GUI.backgroundColor = prevBg;
            EditorGUILayout.EndHorizontal();

            if (!_propEnabled.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "剥离器已禁用。打包时所有 Shader 变体均会保留，包体可能增大。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"剥离器已启用。打包时将移除包含以下关键字的变体（共 {_propKeywords.arraySize} 个）。",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8);

            // ── 关键字列表 ────────────────────────────────────────────
            EditorGUILayout.LabelField("剥离关键字列表", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "包含列表中任意关键字的 Shader 变体将在打包时被移除。\n" +
                "每行一个关键字，关键字名称区分大小写。",
                MessageType.None);
            EditorGUILayout.Space(4);

            // 滚动区域显示关键字列表
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos,
                GUILayout.Height(Mathf.Min(300, _propKeywords.arraySize * 22 + 32)));

            int indexToDelete = -1;
            for (int i = 0; i < _propKeywords.arraySize; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var elem = _propKeywords.GetArrayElementAtIndex(i);
                elem.stringValue = EditorGUILayout.TextField(elem.stringValue);
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("×", GUILayout.Width(24)))
                    indexToDelete = i;
                GUI.backgroundColor = prevBg;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (indexToDelete >= 0)
                _propKeywords.DeleteArrayElementAtIndex(indexToDelete);

            // 添加按钮
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 添加关键字", GUILayout.Height(24)))
            {
                _propKeywords.arraySize++;
                _propKeywords.GetArrayElementAtIndex(_propKeywords.arraySize - 1).stringValue = "";
            }
            if (GUILayout.Button("恢复默认列表", GUILayout.Height(24), GUILayout.Width(110)))
            {
                if (EditorUtility.DisplayDialog("确认",
                    "是否将关键字列表恢复为默认值？当前列表将被覆盖。", "恢复", "取消"))
                {
                    ResetToDefaults(_propKeywords);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(12);

            // 配置文件位置
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("配置文件路径", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(AssetPath, EditorStyles.miniLabel);
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<Object>(AssetPath);
                if (asset != null) { EditorGUIUtility.PingObject(asset); Selection.activeObject = asset; }
            }
            EditorGUILayout.EndHorizontal();

            // 应用修改
            if (_serializedSettings.ApplyModifiedProperties())
            {
                AssetDatabase.SaveAssets();
            }
        }

        private static readonly string[] DefaultKeywords =
        {
            "_SPECULARHIGHLIGHTS_OFF",
            "_ENVIRONMENTREFLECTIONS_OFF",
            "_PARALLAXMAP",
            "_ADDITIONAL_LIGHTS_VERTEX",
            "_FORWARD_PLUS",
            "_SHADOWS_SOFT",
            "_SHADOWS_SOFT_LOW",
            "_SHADOWS_SOFT_MEDIUM",
            "_SHADOWS_SOFT_HIGH",
            "_REFLECTION_PROBE_BLENDING",
            "_REFLECTION_PROBE_BOX_PROJECTION",
            "_SCREEN_SPACE_OCCLUSION",
            "_LIGHT_LAYERS",
            "LOD_FADE_CROSSFADE",
            "DEBUG_DISPLAY"
        };

        private static void ResetToDefaults(SerializedProperty prop)
        {
            prop.arraySize = DefaultKeywords.Length;
            for (int i = 0; i < DefaultKeywords.Length; i++)
                prop.GetArrayElementAtIndex(i).stringValue = DefaultKeywords[i];
        }
    }
}
