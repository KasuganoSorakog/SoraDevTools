using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.TextCore.Text;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UIElements;

namespace MagicBox.Editor
{
    public static class ChineseFontSetup
    {
        private const string FontDir = "Assets/MagicBox/Fonts";
        private const string FontFilePath = "Assets/MagicBox/Fonts/msyh.ttf";
        private const string FontAssetPath = "Assets/MagicBox/Fonts/ChineseFont-SDF.asset";
        private const string TextSettingsPath = "Assets/MagicBox/UI/MagicBoxTextSettings.asset";

        [MenuItem("MagicBox/Setup Chinese Font", false, 10)]
        public static void SetupChineseFont()
        {
            var fontAsset = CreateOrLoadFontAsset();
            if (fontAsset == null)
            {
                Debug.LogError("[MagicBox] Failed to create Chinese font asset.");
                return;
            }

            AddAsFallback(fontAsset);
            Debug.Log("[MagicBox] Chinese font setup complete.");
        }

        public static FontAsset CreateOrLoadFontAsset()
        {
            // Validate existing asset — delete if broken (missing atlas textures)
            var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (existing != null)
            {
                if (existing.atlasTextures != null && existing.atlasTextures.Length > 0 && existing.atlasTextures[0] != null)
                    return existing;

                Debug.LogWarning("[MagicBox] Existing Chinese font asset is broken, recreating...");
                AssetDatabase.DeleteAsset(FontAssetPath);
            }

            // Ensure folder
            if (!AssetDatabase.IsValidFolder(FontDir))
                AssetDatabase.CreateFolder("Assets/MagicBox", "Fonts");

            // Copy system font into project if not already there
            var importedFont = AssetDatabase.LoadAssetAtPath<Font>(FontFilePath);
            if (importedFont == null)
            {
                string srcPath = FindSystemChineseFont();
                if (srcPath == null)
                {
                    Debug.LogError("[MagicBox] No Chinese font found on this system.");
                    return null;
                }

                string fullDest = Path.GetFullPath(FontFilePath);
                File.Copy(srcPath, fullDest, true);
                AssetDatabase.ImportAsset(FontFilePath, ImportAssetOptions.ForceUpdate);
                importedFont = AssetDatabase.LoadAssetAtPath<Font>(FontFilePath);

                if (importedFont == null)
                {
                    Debug.LogError($"[MagicBox] Failed to import font from {srcPath}");
                    return null;
                }
                Debug.Log($"[MagicBox] Copied system font from {srcPath}");
            }

            // Create SDF font asset from the imported font
            var fontAsset = FontAsset.CreateFontAsset(
                importedFont,
                36,
                4,
                GlyphRenderMode.SDFAA,
                2048,
                2048
            );

            if (fontAsset == null)
            {
                Debug.LogError("[MagicBox] FontAsset.CreateFontAsset returned null.");
                return null;
            }

            fontAsset.name = "ChineseFont-SDF";

            // Save main asset
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            // Save atlas textures as sub-assets (critical — without this they get lost)
            if (fontAsset.atlasTextures != null)
            {
                for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
                {
                    var tex = fontAsset.atlasTextures[i];
                    if (tex != null)
                    {
                        tex.name = $"ChineseFont-SDF Atlas {i}";
                        AssetDatabase.AddObjectToAsset(tex, fontAsset);
                    }
                }
            }

            // Save material as sub-asset
            if (fontAsset.material != null)
            {
                fontAsset.material.name = "ChineseFont-SDF Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[MagicBox] Created Chinese font asset at {FontAssetPath}");
            return fontAsset;
        }

        private static string FindSystemChineseFont()
        {
            string fontsDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts);
            // Prefer .ttf over .ttc (Unity imports .ttf more reliably)
            string[] candidates =
            {
                Path.Combine(fontsDir, "msyh.ttf"),        // 微软雅黑 (regular, .ttf variant)
                Path.Combine(fontsDir, "msyhbd.ttf"),       // 微软雅黑 Bold
                Path.Combine(fontsDir, "simhei.ttf"),       // 黑体
                Path.Combine(fontsDir, "simsun.ttc"),       // 宋体
                Path.Combine(fontsDir, "msyh.ttc"),         // 微软雅黑 TTC
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path)) return path;
            }

            // Fallback: scan for any Chinese-capable .ttf
            if (Directory.Exists(fontsDir))
            {
                foreach (var f in Directory.GetFiles(fontsDir, "*.ttf"))
                {
                    string name = Path.GetFileName(f).ToLower();
                    if (name.Contains("yahei") || name.Contains("simhei") || name.Contains("simsun") ||
                        name.Contains("noto") || name.Contains("pingfang") || name.Contains("sourcehansans"))
                        return f;
                }
            }

            return null;
        }

        public static void AddAsFallback(FontAsset fontAsset)
        {
            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (textSettings == null)
            {
                Debug.LogError($"[MagicBox] TextSettings not found at {TextSettingsPath}. Run MagicBox > Setup Scene first.");
                return;
            }

            var so = new SerializedObject(textSettings);
            var fallbackProp = so.FindProperty("m_FallbackFontAssets");

            // Check if already in list
            for (int i = 0; i < fallbackProp.arraySize; i++)
            {
                if (fallbackProp.GetArrayElementAtIndex(i).objectReferenceValue == fontAsset)
                {
                    Debug.Log("[MagicBox] Chinese font already in fallback list.");
                    return;
                }
            }

            fallbackProp.arraySize++;
            fallbackProp.GetArrayElementAtIndex(fallbackProp.arraySize - 1).objectReferenceValue = fontAsset;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[MagicBox] Added Chinese font as fallback in TextSettings.");
        }
    }
}
