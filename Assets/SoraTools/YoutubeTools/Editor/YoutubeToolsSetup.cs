using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using UnityEngine.TextCore.LowLevel;

namespace YoutubeTools.Editor
{
    public static class YoutubeToolsSetup
    {
        private const string UIDir = "Assets/SoraTools/YoutubeTools/UI";
        private const string PanelSettingsPath = "Assets/SoraTools/YoutubeTools/UI/YtToolsPanelSettings.asset";
        private const string TextSettingsPath  = "Assets/SoraTools/YoutubeTools/UI/YtToolsTextSettings.asset";
        private const string MainUssPath       = "Assets/SoraTools/YoutubeTools/UI/Styles/Main.uss";
        private const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        // Reuse MagicBox's Chinese font
        private const string MagicBoxFontPath = "Assets/SoraTools/MagicBox/Fonts/ChineseFont-SDF.asset";

        // Fallback: own font slot
        private const string FontDir     = "Assets/SoraTools/YoutubeTools/Fonts";
        private const string FontFilePath = "Assets/SoraTools/YoutubeTools/Fonts/msyh.ttf";
        private const string FontAssetPath = "Assets/SoraTools/YoutubeTools/Fonts/ChineseFont-SDF.asset";

        [MenuItem("SoraTools/YouTube Tools - Setup Scene", false, 10)]
        public static void SetupScene()
        {
            EnsureDirectory(UIDir);

            // 1. PanelSettings
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1280, 900);
                panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panelSettings.match = 0.5f;
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
                Debug.Log("[YouTubeTools] Created PanelSettings");
            }

            // 2. Runtime theme
            if (panelSettings.themeStyleSheet == null)
            {
                var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
                if (theme != null)
                {
                    panelSettings.themeStyleSheet = theme;
                    EditorUtility.SetDirty(panelSettings);
                }
                else
                {
                    Debug.LogError("[YouTubeTools] Theme not found at: " + ThemePath);
                }
            }

            // 3. PanelTextSettings
            if (panelSettings.textSettings == null)
            {
                var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
                if (textSettings == null)
                {
                    textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
                    AssetDatabase.CreateAsset(textSettings, TextSettingsPath);
                    Debug.Log("[YouTubeTools] Created PanelTextSettings");
                }
                panelSettings.textSettings = textSettings;
                EditorUtility.SetDirty(panelSettings);
            }
            AssetDatabase.SaveAssets();

            // 4. Chinese font
            SetupChineseFont();

            // 5. Camera
            var cam = Object.FindObjectOfType<Camera>();
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.tag = "MainCamera";
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.051f, 0.067f, 0.090f); // #0d1117

            // 6. YoutubeToolsApp GameObject
            var appComp = Object.FindObjectOfType<YoutubeToolsApp>();
            GameObject appGo;
            if (appComp == null)
            {
                appGo = new GameObject("YoutubeToolsApp");
                appComp = appGo.AddComponent<YoutubeToolsApp>();
                Debug.Log("[YouTubeTools] Created YoutubeToolsApp GameObject");
            }
            else
            {
                appGo = appComp.gameObject;
            }

            // 7. UIDocument
            var uiDoc = appGo.GetComponent<UIDocument>();
            if (uiDoc == null)
                uiDoc = appGo.AddComponent<UIDocument>();
            uiDoc.panelSettings = panelSettings;
            uiDoc.sortingOrder  = 0;

            // 8. Assign mainStyleSheet so the USS is tracked as a build dependency
            var mainUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(MainUssPath);
            if (mainUss != null)
            {
                var so = new SerializedObject(appComp);
                so.FindProperty("mainStyleSheet").objectReferenceValue = mainUss;
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning("[YouTubeTools] Main.uss not found at: " + MainUssPath);
            }

            // 9. Mark dirty
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log("[YouTubeTools] Scene setup complete!");
            EditorUtility.DisplayDialog("YouTube Tools Setup",
                "场景配置完成!\n\n" +
                "- PanelSettings: 已创建并挂载主题\n" +
                "- TextSettings: 中文字体已配置\n" +
                "- UIDocument: 已挂载到 YoutubeToolsApp\n\n" +
                "保存场景 (Ctrl+S) 后按 Play。", "确定");
        }

        [MenuItem("SoraTools/YouTube Tools - Configure Build", false, 11)]
        public static void ConfigureBuild()
        {
            PlayerSettings.productName = "YouTube Tools";
            PlayerSettings.companyName = "SoraTools";
            PlayerSettings.defaultScreenWidth  = 1280;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.displayResolutionDialog = ResolutionDialogSetting.Disabled;

            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scene.path, true) };

            EditorUtility.DisplayDialog("Build Settings",
                "构建设置配置完成!\n\n分辨率: 1280×900 窗口模式\n场景: " + scene.name, "确定");
        }

        // ── Chinese Font ──

        private static void SetupChineseFont()
        {
            // Prefer MagicBox's font asset (already generated)
            var fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(MagicBoxFontPath);
            if (fontAsset != null && fontAsset.atlasTextures != null &&
                fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
            {
                Debug.Log("[YouTubeTools] Reusing MagicBox Chinese font");
                AddFontAsFallback(fontAsset);
                return;
            }

            // Try own font asset
            fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (fontAsset != null && fontAsset.atlasTextures != null &&
                fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
            {
                AddFontAsFallback(fontAsset);
                return;
            }

            // Create from system font
            if (fontAsset != null)
                AssetDatabase.DeleteAsset(FontAssetPath);

            EnsureDirectory(FontDir);

            var importedFont = AssetDatabase.LoadAssetAtPath<Font>(FontFilePath);
            if (importedFont == null)
            {
                string srcPath = FindSystemChineseFont();
                if (srcPath == null)
                {
                    Debug.LogWarning("[YouTubeTools] No Chinese font found. English only.");
                    return;
                }
                File.Copy(srcPath, Path.GetFullPath(FontFilePath), true);
                AssetDatabase.ImportAsset(FontFilePath, ImportAssetOptions.ForceUpdate);
                importedFont = AssetDatabase.LoadAssetAtPath<Font>(FontFilePath);
                if (importedFont == null) { Debug.LogError("[YouTubeTools] Font import failed"); return; }
            }

            fontAsset = FontAsset.CreateFontAsset(importedFont, 36, 4, GlyphRenderMode.SDFAA, 2048, 2048);
            if (fontAsset == null) { Debug.LogError("[YouTubeTools] FontAsset creation failed"); return; }

            fontAsset.name = "ChineseFont-SDF";
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            if (fontAsset.atlasTextures != null)
            {
                for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
                {
                    var tex = fontAsset.atlasTextures[i];
                    if (tex != null)
                    {
                        tex.name = "ChineseFont-SDF Atlas " + i;
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
            AddFontAsFallback(fontAsset);
        }

        private static void AddFontAsFallback(FontAsset fontAsset)
        {
            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (textSettings == null) { Debug.LogError("[YouTubeTools] TextSettings not found."); return; }

            var so = new SerializedObject(textSettings);
            var fallbackProp = so.FindProperty("m_FallbackFontAssets");

            for (int i = 0; i < fallbackProp.arraySize; i++)
            {
                if (fallbackProp.GetArrayElementAtIndex(i).objectReferenceValue == fontAsset)
                    return; // already present
            }

            fallbackProp.arraySize++;
            fallbackProp.GetArrayElementAtIndex(fallbackProp.arraySize - 1).objectReferenceValue = fontAsset;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[YouTubeTools] Added Chinese font as fallback");
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
            foreach (var p in candidates)
                if (File.Exists(p)) return p;

            if (Directory.Exists(fontsDir))
            {
                foreach (var f in Directory.GetFiles(fontsDir, "*.ttf"))
                {
                    string n = Path.GetFileName(f).ToLower();
                    if (n.Contains("yahei") || n.Contains("simhei") || n.Contains("simsun") ||
                        n.Contains("noto")  || n.Contains("sourcehansans"))
                        return f;
                }
            }
            return null;
        }

        private static void EnsureDirectory(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string[] parts = assetPath.Split('/');
            string current = parts[0];
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
