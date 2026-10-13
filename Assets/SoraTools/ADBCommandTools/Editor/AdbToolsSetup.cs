using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using UnityEngine.TextCore.LowLevel;

namespace SoraTools.Editor
{
    public static class AdbToolsSetup
    {
        private const string UIDir = "Assets/ADBCommandTools/UI";
        private const string PanelSettingsPath = "Assets/ADBCommandTools/UI/AdbToolsPanelSettings.asset";
        private const string TextSettingsPath = "Assets/ADBCommandTools/UI/AdbToolsTextSettings.asset";
        private const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        // Font paths
        private const string FontDir = "Assets/ADBCommandTools/Fonts";
        private const string FontFilePath = "Assets/ADBCommandTools/Fonts/msyh.ttf";
        private const string FontAssetPath = "Assets/ADBCommandTools/Fonts/ChineseFont-SDF.asset";

        // Try to reuse MagicBox's font if available
        private const string MagicBoxFontAssetPath = "Assets/MagicBox/Fonts/ChineseFont-SDF.asset";

        [MenuItem("SoraTools/ADB Tools - Setup Scene", false, 1)]
        public static void SetupScene()
        {
            // Ensure UI directory
            EnsureDirectory(UIDir);

            // 1. Create or load PanelSettings
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1280, 800);
                panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panelSettings.match = 0.5f;
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
                Debug.Log("[AdbTools] Created PanelSettings");
            }

            // 2. Assign default runtime theme (provides built-in font)
            if (panelSettings.themeStyleSheet == null)
            {
                var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
                if (theme != null)
                {
                    panelSettings.themeStyleSheet = theme;
                    EditorUtility.SetDirty(panelSettings);
                    Debug.Log("[AdbTools] Assigned UnityDefaultRuntimeTheme");
                }
                else
                {
                    Debug.LogError("[AdbTools] Theme not found at: " + ThemePath);
                }
            }

            // 3. Create and assign PanelTextSettings for Chinese font support
            if (panelSettings.textSettings == null)
            {
                var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
                if (textSettings == null)
                {
                    textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
                    AssetDatabase.CreateAsset(textSettings, TextSettingsPath);
                    Debug.Log("[AdbTools] Created PanelTextSettings");
                }
                panelSettings.textSettings = textSettings;
                EditorUtility.SetDirty(panelSettings);
            }
            AssetDatabase.SaveAssets();

            // 4. Setup Chinese font fallback
            SetupChineseFont();

            // 5. Find or create Camera
            var cam = Object.FindObjectOfType<Camera>();
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.tag = "MainCamera";
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.10f, 0.18f);

            // 6. Find or create AdbToolsApp GameObject
            var appComponent = Object.FindObjectOfType<AdbToolsApp>();
            GameObject appGo;
            if (appComponent == null)
            {
                appGo = new GameObject("AdbToolsApp");
                appComponent = appGo.AddComponent<AdbToolsApp>();
                Debug.Log("[AdbTools] Created AdbToolsApp GameObject");
            }
            else
            {
                appGo = appComponent.gameObject;
            }

            // 7. Ensure UIDocument and assign PanelSettings
            var uiDoc = appGo.GetComponent<UIDocument>();
            if (uiDoc == null)
                uiDoc = appGo.AddComponent<UIDocument>();
            uiDoc.panelSettings = panelSettings;
            uiDoc.sortingOrder = 0;

            // 8. Mark scene dirty
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            AssetDatabase.SaveAssets();

            Debug.Log("[AdbTools] Scene setup complete!");
            EditorUtility.DisplayDialog("ADB Tools Setup",
                "Scene setup complete!\n\n" +
                "- PanelSettings: created with theme\n" +
                "- TextSettings: Chinese font configured\n" +
                "- UIDocument: configured on AdbToolsApp\n\n" +
                "Save the scene (Ctrl+S) and press Play.", "OK");
        }

        [MenuItem("SoraTools/ADB Tools - Configure Build", false, 2)]
        public static void ConfigureBuild()
        {
            PlayerSettings.productName = "ADB Command Tools";
            PlayerSettings.companyName = "SoraTools";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.forceSingleInstance = true;

            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.displayResolutionDialog = ResolutionDialogSetting.Disabled;

            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path))
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scene.path, true) };
            }

            EditorUtility.DisplayDialog("Build Settings",
                 $"Build settings configured!\n\n" +
                 $"Resolution: 1280x800 Windowed\n" +
                 $"Single Instance: Enabled\n" +
                 $"Scene: {scene.name}", "OK");
        }

        // ── Chinese Font Setup ──

        private static void SetupChineseFont()
        {
            // Try reuse MagicBox's font asset first
            var fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(MagicBoxFontAssetPath);
            if (fontAsset != null && fontAsset.atlasTextures != null &&
                fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
            {
                Debug.Log("[AdbTools] Reusing MagicBox Chinese font asset");
                AddFontAsFallback(fontAsset);
                return;
            }

            // Try load our own font asset
            fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (fontAsset != null && fontAsset.atlasTextures != null &&
                fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
            {
                AddFontAsFallback(fontAsset);
                return;
            }

            // Need to create font asset from scratch
            if (fontAsset != null)
                AssetDatabase.DeleteAsset(FontAssetPath);

            EnsureDirectory(FontDir);

            // Import system font if needed
            var importedFont = AssetDatabase.LoadAssetAtPath<Font>(FontFilePath);
            if (importedFont == null)
            {
                string srcPath = FindSystemChineseFont();
                if (srcPath == null)
                {
                    Debug.LogWarning("[AdbTools] No Chinese font found on system. English only.");
                    return;
                }

                string fullDest = Path.GetFullPath(FontFilePath);
                File.Copy(srcPath, fullDest, true);
                AssetDatabase.ImportAsset(FontFilePath, ImportAssetOptions.ForceUpdate);
                importedFont = AssetDatabase.LoadAssetAtPath<Font>(FontFilePath);
                if (importedFont == null)
                {
                    Debug.LogError("[AdbTools] Failed to import font");
                    return;
                }
                Debug.Log($"[AdbTools] Copied system font from {srcPath}");
            }

            // Create SDF font asset
            fontAsset = FontAsset.CreateFontAsset(
                importedFont, 36, 4, GlyphRenderMode.SDFAA, 2048, 2048);

            if (fontAsset == null)
            {
                Debug.LogError("[AdbTools] FontAsset.CreateFontAsset returned null");
                return;
            }

            fontAsset.name = "ChineseFont-SDF";
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            // Save atlas textures as sub-assets
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

            if (fontAsset.material != null)
            {
                fontAsset.material.name = "ChineseFont-SDF Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[AdbTools] Created Chinese font asset at {FontAssetPath}");

            AddFontAsFallback(fontAsset);
        }

        private static void AddFontAsFallback(FontAsset fontAsset)
        {
            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (textSettings == null)
            {
                Debug.LogError("[AdbTools] TextSettings not found. Run Setup Scene first.");
                return;
            }

            var so = new SerializedObject(textSettings);
            var fallbackProp = so.FindProperty("m_FallbackFontAssets");

            // Check if already present
            for (int i = 0; i < fallbackProp.arraySize; i++)
            {
                if (fallbackProp.GetArrayElementAtIndex(i).objectReferenceValue == fontAsset)
                    return;
            }

            fallbackProp.arraySize++;
            fallbackProp.GetArrayElementAtIndex(fallbackProp.arraySize - 1).objectReferenceValue = fontAsset;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[AdbTools] Added Chinese font as fallback");
        }

        private static string FindSystemChineseFont()
        {
            string fontsDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts);
            string[] candidates =
            {
                Path.Combine(fontsDir, "msyh.ttf"),
                Path.Combine(fontsDir, "msyhbd.ttf"),
                Path.Combine(fontsDir, "simhei.ttf"),
                Path.Combine(fontsDir, "simsun.ttc"),
                Path.Combine(fontsDir, "msyh.ttc"),
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path)) return path;
            }

            if (Directory.Exists(fontsDir))
            {
                foreach (var f in Directory.GetFiles(fontsDir, "*.ttf"))
                {
                    string name = Path.GetFileName(f).ToLower();
                    if (name.Contains("yahei") || name.Contains("simhei") || name.Contains("simsun") ||
                        name.Contains("noto") || name.Contains("sourcehansans"))
                        return f;
                }
            }

            return null;
        }

        private static void EnsureDirectory(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            string[] parts = assetPath.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
