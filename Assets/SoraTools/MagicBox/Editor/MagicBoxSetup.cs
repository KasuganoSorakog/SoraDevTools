using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;

namespace MagicBox.Editor
{
    public static class MagicBoxSetup
    {
        [MenuItem("MagicBox/Setup Scene", false, 1)]
        public static void SetupScene()
        {
            // 1. Create PanelSettings if needed
            string panelSettingsPath = "Assets/MagicBox/UI/MagicBoxPanelSettings.asset";
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1280, 720);
                panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panelSettings.match = 0.5f;
                AssetDatabase.CreateAsset(panelSettings, panelSettingsPath);
                Debug.Log("[MagicBox] Created PanelSettings at " + panelSettingsPath);
            }

            // 1b. Assign default runtime theme (provides built-in font for text rendering)
            if (panelSettings.themeStyleSheet == null)
            {
                string themePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";
                var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
                if (theme != null)
                {
                    panelSettings.themeStyleSheet = theme;
                    EditorUtility.SetDirty(panelSettings);
                    Debug.Log("[MagicBox] Assigned default runtime theme to PanelSettings");
                }
            }

            // 1c. Create and assign PanelTextSettings if missing
            if (panelSettings.textSettings == null)
            {
                string textSettingsPath = "Assets/MagicBox/UI/MagicBoxTextSettings.asset";
                var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(textSettingsPath);
                if (textSettings == null)
                {
                    textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
                    AssetDatabase.CreateAsset(textSettings, textSettingsPath);
                    Debug.Log("[MagicBox] Created PanelTextSettings at " + textSettingsPath);
                }
                panelSettings.textSettings = textSettings;
                EditorUtility.SetDirty(panelSettings);
                Debug.Log("[MagicBox] Assigned TextSettings to PanelSettings");
            }
            AssetDatabase.SaveAssets();

            // 1d. Setup Chinese font for CJK support
            ChineseFontSetup.SetupChineseFont();

            // 2. Load StyleSheets
            string varPath = "Assets/MagicBox/UI/Styles/Variables.uss";
            string mainPath = "Assets/MagicBox/UI/Styles/Main.uss";
            var variablesStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(varPath);
            var mainStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(mainPath);
            if (variablesStyle == null) Debug.LogError("[MagicBox] Variables.uss not found at " + varPath);
            if (mainStyle == null) Debug.LogError("[MagicBox] Main.uss not found at " + mainPath);

            // 3. Find or create Camera
            var cam = Object.FindObjectOfType<Camera>();
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.tag = "MainCamera";
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.18f);

            // 4. Find or create MagicBoxApp GameObject
            var appComponent = Object.FindObjectOfType<MagicBox.UI.MagicBoxApp>();
            GameObject appGo;
            if (appComponent == null)
            {
                appGo = new GameObject("MagicBoxApp");
                appComponent = appGo.AddComponent<MagicBox.UI.MagicBoxApp>();
                Debug.Log("[MagicBox] Created MagicBoxApp GameObject");
            }
            else
            {
                appGo = appComponent.gameObject;
            }

            // 5. Ensure UIDocument
            var uiDoc = appGo.GetComponent<UIDocument>();
            if (uiDoc == null)
                uiDoc = appGo.AddComponent<UIDocument>();
            uiDoc.panelSettings = panelSettings;
            uiDoc.sortingOrder = 0;

            // 6. Assign StyleSheet references via SerializedObject
            var so = new SerializedObject(appComponent);
            var varProp = so.FindProperty("variablesStyle");
            var mainProp = so.FindProperty("mainStyle");
            if (varProp != null && variablesStyle != null) varProp.objectReferenceValue = variablesStyle;
            if (mainProp != null && mainStyle != null) mainProp.objectReferenceValue = mainStyle;
            so.ApplyModifiedProperties();

            // 7. Mark scene dirty
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Debug.Log("[MagicBox] Scene setup complete! Save the scene and configure Build Settings.");
            EditorUtility.DisplayDialog("MagicBox Setup",
                "Scene setup complete!\n\n" +
                "- PanelSettings created\n" +
                "- UIDocument configured\n" +
                "- StyleSheets assigned\n\n" +
                "Next steps:\n" +
                "1. Save the scene (Ctrl+S)\n" +
                "2. Run MagicBox > Configure Build Settings\n" +
                "3. Build (File > Build And Run)", "OK");
        }

        [MenuItem("MagicBox/Configure Build Settings", false, 2)]
        public static void ConfigureBuildSettings()
        {
            // Player Settings
            PlayerSettings.productName = "MagicBox";
            PlayerSettings.companyName = "MagicBox";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard);

            // Disable splash screen (requires Unity Pro, but set it regardless)
            PlayerSettings.SplashScreen.show = false;

            // Force windowed mode — disable resolution dialog and fullscreen by default
            PlayerSettings.displayResolutionDialog = ResolutionDialogSetting.Disabled;

            // Set the scene in build settings
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path))
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scene.path, true) };
                Debug.Log("[MagicBox] Build settings configured with scene: " + scene.path);
            }

            EditorUtility.DisplayDialog("MagicBox Build Settings",
                "Build settings configured!\n\n" +
                $"Product: {PlayerSettings.productName}\n" +
                $"Resolution: {PlayerSettings.defaultScreenWidth}x{PlayerSettings.defaultScreenHeight}\n" +
                "Mode: Windowed, Resizable\n\n" +
                "You can now build via File > Build And Run\n" +
                "Target: Windows x86_64", "OK");
        }

        [MenuItem("MagicBox/Quick Build", false, 3)]
        public static void QuickBuild()
        {
            string buildPath = EditorUtility.SaveFolderPanel("Choose Build Output Folder", "", "MagicBoxBuild");
            if (string.IsNullOrEmpty(buildPath)) return;

            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            string[] scenes = { scene.path };

            string exePath = System.IO.Path.Combine(buildPath, "MagicBox.exe");
            var report = BuildPipeline.BuildPlayer(scenes, exePath, BuildTarget.StandaloneWindows64,
                BuildOptions.None);

            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[MagicBox] Build succeeded: {exePath} ({report.summary.totalSize / (1024 * 1024):F1} MB)");
                EditorUtility.RevealInFinder(exePath);
            }
            else
            {
                Debug.LogError($"[MagicBox] Build failed: {report.summary.result}");
            }
        }
    }
}
