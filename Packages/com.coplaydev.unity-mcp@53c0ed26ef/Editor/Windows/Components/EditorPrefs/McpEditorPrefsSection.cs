using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Preferences
{
    /// <summary>
    /// Reusable EditorPrefs editor panel for UnityMCP settings diagnostics.
    /// </summary>
    public sealed class McpEditorPrefsSection
    {
        private readonly List<EditorPrefItem> currentPrefs = new();
        private readonly HashSet<string> knownMcpKeys = new();
        private readonly Dictionary<string, EditorPrefType> knownPrefTypes = new()
        {
            { EditorPrefKeys.DebugLogs, EditorPrefType.Bool },
            { EditorPrefKeys.UseHttpTransport, EditorPrefType.Bool },
            { EditorPrefKeys.ResumeHttpAfterReload, EditorPrefType.Bool },
            { EditorPrefKeys.ResumeStdioAfterReload, EditorPrefType.Bool },
            { EditorPrefKeys.UseEmbeddedServer, EditorPrefType.Bool },
            { EditorPrefKeys.LockCursorConfig, EditorPrefType.Bool },
            { EditorPrefKeys.AutoRegisterEnabled, EditorPrefType.Bool },
            { EditorPrefKeys.SetupCompleted, EditorPrefType.Bool },
            { EditorPrefKeys.SetupDismissed, EditorPrefType.Bool },
            { EditorPrefKeys.CustomToolRegistrationEnabled, EditorPrefType.Bool },
            { EditorPrefKeys.TelemetryDisabled, EditorPrefType.Bool },
            { EditorPrefKeys.DevModeForceServerRefresh, EditorPrefType.Bool },
            { EditorPrefKeys.UnitySocketPort, EditorPrefType.Int },
            { EditorPrefKeys.ValidationLevel, EditorPrefType.Int },
            { EditorPrefKeys.LastUpdateCheck, EditorPrefType.Int },
            { EditorPrefKeys.LastStdIoUpgradeVersion, EditorPrefType.Int },
            { EditorPrefKeys.EditorWindowActivePanel, EditorPrefType.String },
            { EditorPrefKeys.ClaudeCliPathOverride, EditorPrefType.String },
            { EditorPrefKeys.UvxPathOverride, EditorPrefType.String },
            { EditorPrefKeys.HttpBaseUrl, EditorPrefType.String },
            { EditorPrefKeys.HttpTransportScope, EditorPrefType.String },
            { EditorPrefKeys.SessionId, EditorPrefType.String },
            { EditorPrefKeys.WebSocketUrlOverride, EditorPrefType.String },
            { EditorPrefKeys.GitUrlOverride, EditorPrefType.String },
            { EditorPrefKeys.PackageDeploySourcePath, EditorPrefType.String },
            { EditorPrefKeys.PackageDeployLastBackupPath, EditorPrefType.String },
            { EditorPrefKeys.PackageDeployLastTargetPath, EditorPrefType.String },
            { EditorPrefKeys.PackageDeployLastSourcePath, EditorPrefType.String },
            { EditorPrefKeys.ServerSrc, EditorPrefType.String },
            { EditorPrefKeys.LatestKnownVersion, EditorPrefType.String },
        };

        private VisualElement prefsContainer;
        private VisualTreeAsset itemTemplate;

        public McpEditorPrefsSection(VisualElement root, VisualTreeAsset itemTemplate)
        {
            Root = root;
            this.itemTemplate = itemTemplate;
            prefsContainer = Root.Q<VisualElement>("prefs-container");
            LoadKnownMcpKeys();
            Refresh();
        }

        public VisualElement Root { get; }

        public void Refresh()
        {
            if (prefsContainer == null)
            {
                return;
            }

            currentPrefs.Clear();
            prefsContainer.Clear();

            var allKeys = new List<string>();
            allKeys.AddRange(knownMcpKeys);

            foreach (var key in GetAllMcpKeys())
            {
                if (!allKeys.Contains(key))
                {
                    allKeys.Add(key);
                }
            }

            allKeys.Sort();
            foreach (var key in allKeys)
            {
                if (key == EditorPrefKeys.CustomerUuid)
                {
                    continue;
                }

                var item = CreateEditorPrefItem(key);
                if (item == null)
                {
                    continue;
                }

                currentPrefs.Add(item);
                prefsContainer.Add(CreateItemUI(item));
            }
        }

        private void LoadKnownMcpKeys()
        {
            knownMcpKeys.Clear();
            var fields = typeof(EditorPrefKeys).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var field in fields)
            {
                if (field.IsLiteral && !field.IsInitOnly)
                {
                    knownMcpKeys.Add(field.GetValue(null).ToString());
                }
            }
        }

        private static List<string> GetAllMcpKeys()
        {
            var keys = new List<string> { "MCPForUnity.TestKey" };
            return keys.Where(EditorPrefs.HasKey).ToList();
        }

        private EditorPrefItem CreateEditorPrefItem(string key)
        {
            var item = new EditorPrefItem { Key = key, IsKnown = knownMcpKeys.Contains(key) };
            if (knownPrefTypes.TryGetValue(key, out var knownType))
            {
                item.Type = knownType;
                item.Value = knownType switch
                {
                    EditorPrefType.Bool => EditorPrefs.GetBool(key, false).ToString(),
                    EditorPrefType.Int => EditorPrefs.GetInt(key, 0).ToString(),
                    EditorPrefType.Float => EditorPrefs.GetFloat(key, 0f).ToString(),
                    _ => EditorPrefs.GetString(key, "")
                };
                return item;
            }

            if (!EditorPrefs.HasKey(key))
            {
                return null;
            }

            var stringValue = EditorPrefs.GetString(key, "");
            if (int.TryParse(stringValue, out var intValue))
            {
                item.Type = EditorPrefType.Int;
                item.Value = intValue.ToString();
            }
            else if (float.TryParse(stringValue, out var floatValue))
            {
                item.Type = EditorPrefType.Float;
                item.Value = floatValue.ToString();
            }
            else if (bool.TryParse(stringValue, out var boolValue))
            {
                item.Type = EditorPrefType.Bool;
                item.Value = boolValue.ToString();
            }
            else
            {
                item.Type = EditorPrefType.String;
                item.Value = stringValue;
            }

            return item;
        }

        private VisualElement CreateItemUI(EditorPrefItem item)
        {
            if (itemTemplate == null)
            {
                McpLog.Error("EditorPrefs 条目模板尚未加载");
                return new VisualElement();
            }

            var itemElement = itemTemplate.CloneTree();
            itemElement.Q<Label>("key-label").text = item.Key;

            var valueField = itemElement.Q<TextField>("value-field");
            valueField.value = item.Value;

            var typeDropdown = itemElement.Q<DropdownField>("type-dropdown");
            typeDropdown.index = (int)item.Type;

            var saveButton = itemElement.Q<Button>("save-button");
            saveButton.clicked += () => SavePref(item, valueField.value, (EditorPrefType)typeDropdown.index);

            return itemElement;
        }

        private void SavePref(EditorPrefItem item, string newValue, EditorPrefType newType)
        {
            SaveValue(item.Key, newValue, newType);
            Refresh();
        }

        private static void SaveValue(string key, string value, EditorPrefType type)
        {
            switch (type)
            {
                case EditorPrefType.String:
                    EditorPrefs.SetString(key, value);
                    break;
                case EditorPrefType.Int:
                    if (int.TryParse(value, out var intValue))
                    {
                        EditorPrefs.SetInt(key, intValue);
                        break;
                    }
                    EditorUtility.DisplayDialog("保存失败", $"无法将 “{value}” 转换为整数。", "确定");
                    break;
                case EditorPrefType.Float:
                    if (float.TryParse(value, out var floatValue))
                    {
                        EditorPrefs.SetFloat(key, floatValue);
                        break;
                    }
                    EditorUtility.DisplayDialog("保存失败", $"无法将 “{value}” 转换为浮点数。", "确定");
                    break;
                case EditorPrefType.Bool:
                    if (bool.TryParse(value, out var boolValue))
                    {
                        EditorPrefs.SetBool(key, boolValue);
                        break;
                    }
                    EditorUtility.DisplayDialog("保存失败", $"无法将 “{value}” 转换为布尔值，请使用 True 或 False。", "确定");
                    break;
            }
        }
    }

    public sealed class EditorPrefItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public EditorPrefType Type { get; set; }
        public bool IsKnown { get; set; }
    }

    public enum EditorPrefType
    {
        String,
        Int,
        Float,
        Bool
    }
}
