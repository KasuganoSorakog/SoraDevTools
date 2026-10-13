using System.IO;
using UnityEngine;
using UnityEditor;

namespace YtDownloader.Editor
{
    /// <summary>
    /// Editor menu items for setting up a YtDownloader scene and build configuration.
    /// Does NOT require UIToolkit — uses plain IMGUI (OnGUI) at runtime.
    /// Compatible with Unity 2019.4 LTS through Unity 2022.3 LTS.
    /// </summary>
    public static class YtDownloaderSetup
    {
        private const string AppDir   = "Assets/SoraTools/YtDownloader";
        private const string FontDir  = "Assets/SoraTools/YtDownloader/Fonts";
        private const string FontPath = "Assets/SoraTools/YtDownloader/Fonts/ChineseFont.ttf";

        // ── Menu items ──

        [MenuItem("SoraTools/YtDownloader - Open Scene", false, 20)]
        public static void OpenScene()
        {
            string scenePath = FindScenePath();
            if (string.IsNullOrEmpty(scenePath))
            {
                EditorUtility.DisplayDialog("YtDownloader",
                    "未找到场景文件。请先执行「Setup Scene」创建场景。", "确定");
                return;
            }

            if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
        }

        [MenuItem("SoraTools/YtDownloader - Setup Scene", false, 21)]
        public static void SetupScene()
        {
            EnsureDirectory(FontDir);

            // 1. Import Chinese font (regular IMGUI Font, not TextMeshPro FontAsset)
            Font chineseFont = ImportChineseFont();

            // 2. Camera
            var cam = Object.FindObjectOfType<Camera>();
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.tag = "MainCamera";
                Debug.Log("[YtDownloader] Created Main Camera.");
            }
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = new Color(0.051f, 0.051f, 0.129f); // matches C_BG

            // 3. YtDownloaderApp GameObject
            var appComp = Object.FindObjectOfType<YtDownloaderApp>();
            if (appComp == null)
            {
                var go = new GameObject("YtDownloaderApp");
                appComp = go.AddComponent<YtDownloaderApp>();
                Debug.Log("[YtDownloader] Created YtDownloaderApp GameObject.");
            }

            // 4. Assign Chinese font via SerializedObject
            string fontMsg = "- 未找到中文字体（英文界面可正常运行）";
            if (chineseFont != null)
            {
                var so = new SerializedObject(appComp);
                so.FindProperty("chineseFont").objectReferenceValue = chineseFont;
                so.ApplyModifiedProperties();
                fontMsg = "- 中文字体已挂载: " + chineseFont.name;
            }

            // 5. Mark scene dirty
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log("[YtDownloader] Scene setup complete.");
            EditorUtility.DisplayDialog("YtDownloader Setup",
                "场景配置完成!\n\n" +
                "- Camera 已设置 (深色背景)\n" +
                "- YtDownloaderApp 已挂载\n" +
                fontMsg + "\n\n" +
                "请保存场景 (Ctrl+S)，然后按 Play 运行。\n" +
                "首次运行时会提示自动下载 yt-dlp + ffmpeg。",
                "确定");
        }

        [MenuItem("SoraTools/YtDownloader - Configure Build", false, 22)]
        public static void ConfigureBuild()
        {
            PlayerSettings.productName        = "YouTube Downloader";
            PlayerSettings.companyName        = "SoraTools";
            PlayerSettings.defaultScreenWidth  = 1280;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode      = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow     = true;
            PlayerSettings.runInBackground     = true;
            PlayerSettings.SplashScreen.show   = false;

#if UNITY_2019_3_OR_NEWER
            PlayerSettings.displayResolutionDialog = ResolutionDialogSetting.Disabled;
#endif

            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path))
                EditorBuildSettings.scenes = new[]
                    { new EditorBuildSettingsScene(scene.path, true) };

            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("YtDownloader Build Settings",
                "构建设置配置完成!\n\n" +
                "分辨率: 1280 × 900 (窗口模式)\n" +
                "平台: Windows PC (Standalone)\n" +
                "场景: " + (string.IsNullOrEmpty(scene.path) ? "(未设置)" : scene.name) + "\n\n" +
                "File → Build Settings → Build 即可打包。",
                "确定");
        }

        // ─────────────────────────────────────────────────────────────────
        // Chinese font import helpers
        // ─────────────────────────────────────────────────────────────────

        private static Font ImportChineseFont()
        {
            // Try to load an already-imported font first
            var existing = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (existing != null)
            {
                Debug.Log("[YtDownloader] Reusing existing Chinese font: " + FontPath);
                return existing;
            }

            // Find a system Chinese font
            string srcPath = FindSystemChineseFont();
            if (srcPath == null)
            {
                Debug.LogWarning("[YtDownloader] No Chinese system font found. " +
                    "Place a TTF file at " + FontPath + " manually.");
                return null;
            }

            // Copy to project and import
            try
            {
                string fullDest = Path.GetFullPath(FontPath);
                File.Copy(srcPath, fullDest, true);
                AssetDatabase.ImportAsset(FontPath, ImportAssetOptions.ForceUpdate);

                var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
                if (font != null)
                    Debug.Log("[YtDownloader] Imported Chinese font from: " + srcPath);
                else
                    Debug.LogError("[YtDownloader] Font import failed for: " + FontPath);
                return font;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[YtDownloader] Font copy/import failed: " + ex.Message);
                return null;
            }
        }

        private static string FindSystemChineseFont()
        {
            string fontsDir = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.Fonts);

            // Preferred candidates (most common Chinese fonts on Windows)
            string[] candidates =
            {
                Path.Combine(fontsDir, "msyh.ttf"),      // Microsoft YaHei
                Path.Combine(fontsDir, "msyhbd.ttf"),
                Path.Combine(fontsDir, "simhei.ttf"),    // SimHei
                Path.Combine(fontsDir, "simsun.ttc"),    // SimSun (TTC works too)
                Path.Combine(fontsDir, "msyh.ttc"),
                Path.Combine(fontsDir, "MSYH.TTF"),
            };

            foreach (var p in candidates)
                if (File.Exists(p)) return p;

            // Fallback: scan for any font with a recognisable Chinese name
            if (Directory.Exists(fontsDir))
            {
                foreach (var f in Directory.GetFiles(fontsDir, "*.ttf"))
                {
                    string n = Path.GetFileName(f).ToLower();
                    if (n.Contains("yahei") || n.Contains("simhei") ||
                        n.Contains("simsun") || n.Contains("noto") ||
                        n.Contains("sourcehansans") || n.Contains("notosans"))
                        return f;
                }
            }

            return null;
        }

        // ─────────────────────────────────────────────────────────────────
        // Utility
        // ─────────────────────────────────────────────────────────────────

        private static string FindScenePath()
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { AppDir });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) return path;
            }
            return null;
        }

        private static void EnsureDirectory(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string[] parts   = assetPath.Split('/');
            string   current = parts[0];
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
