using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Read-only project health report for common VibeCoding failure modes.
    /// </summary>
    [McpForUnityTool("project_health_check", AutoRegister = true)]
    public static class ProjectHealthCheck
    {
        private const int DefaultLimit = 100;
        private const int MaxLimit = 500;
        private const int DefaultMaxScanFiles = 2000;
        private const int MaxScanFiles = 20000;

        public sealed class Parameters
        {
            [ToolParameter("Scope: quick, scene, assets, build, mcp, or full.", Required = false, DefaultValue = "quick")]
            public string scope { get; set; }

            [ToolParameter("Maximum issue count per section. Defaults to 100, capped at 500.", Required = false, DefaultValue = "100")]
            public int? limit { get; set; }

            [ToolParameter("When true, scan prefab/scene/asset text files for missing script markers. Can be slow.", Required = false, DefaultValue = "false")]
            public bool? deep_scan_assets { get; set; }

            [ToolParameter("Asset root for deep scans. Defaults to Assets.", Required = false, DefaultValue = "Assets")]
            public string search_root { get; set; }

            [ToolParameter("Maximum text asset files to scan during deep asset checks. Defaults to 2000, capped at 20000.", Required = false, DefaultValue = "2000")]
            public int? max_scan_files { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string scope = (@params?["scope"]?.ToString() ?? "quick").Trim().ToLowerInvariant();
            int limit = Mathf.Clamp(@params?["limit"]?.ToObject<int?>() ?? DefaultLimit, 1, MaxLimit);
            bool deepScanAssets = @params?["deep_scan_assets"]?.ToObject<bool?>()
                ?? @params?["deepScanAssets"]?.ToObject<bool?>()
                ?? scope is "assets" or "full";
            string searchRoot = NormalizeSearchRoot(GetString(@params, "search_root", "searchRoot"));
            int maxScanFiles = GetMaxScanFiles(@params);

            try
            {
                var report = new JObject
                {
                    ["generated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["scope"] = scope,
                    ["project"] = new JObject
                    {
                        ["name"] = ProjectIdentityUtility.GetProjectName(),
                        ["path"] = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                        ["unity_version"] = Application.unityVersion,
                        ["is_playing"] = Application.isPlaying,
                        ["is_compiling"] = EditorApplication.isCompiling,
                        ["is_updating"] = EditorApplication.isUpdating
                    }
                };

                if (scope is "quick" or "scene" or "full")
                {
                    report["scene"] = BuildSceneHealth(limit);
                }

                if (scope is "quick" or "build" or "full")
                {
                    report["build_settings"] = BuildSettingsHealth(limit);
                    report["asmdefs"] = BuildAsmdefHealth(limit);
                }

                if (scope is "quick" or "mcp" or "full")
                {
                    report["unity_mcp"] = BuildMcpHealth();
                }

                if (deepScanAssets)
                {
                    report["asset_scan"] = BuildAssetHealth(searchRoot, limit, maxScanFiles);
                }
                else
                {
                    report["asset_scan"] = new JObject
                    {
                        ["skipped"] = true,
                        ["reason"] = "deep_scan_assets=false。需要扫描 Prefab/Scene/Asset 文本时显式开启。"
                    };
                }

                report["summary"] = BuildSummary(report);
                return new SuccessResponse("Project health check completed.", report);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[project_health_check] failed: {ex}");
                return new ErrorResponse($"project_health_check failed: {ex.Message}");
            }
        }

        private static JObject BuildSceneHealth(int limit)
        {
            var activeScene = SceneManager.GetActiveScene();
            var objects = EnumerateSceneObjects(includeInactive: true).ToList();
            var missingScripts = new JArray();
            var missingReferences = new JArray();
            int missingScriptCount = 0;
            int missingReferenceCount = 0;

            foreach (var go in objects)
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                missingScriptCount += missing;
                if (missing > 0 && missingScripts.Count < limit)
                {
                    missingScripts.Add(new JObject
                    {
                        ["game_object"] = GetHierarchyPath(go),
                        ["instance_id"] = go.GetInstanceID(),
                        ["missing_count"] = missing
                    });
                }

                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    SerializedObject so;
                    try { so = new SerializedObject(component); }
                    catch { continue; }

                    var iterator = so.GetIterator();
                    while (iterator.NextVisible(true))
                    {
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                        {
                            continue;
                        }

                        if (iterator.objectReferenceValue == null && iterator.objectReferenceInstanceIDValue != 0)
                        {
                            missingReferenceCount++;
                            if (missingReferences.Count < limit)
                            {
                                missingReferences.Add(new JObject
                                {
                                    ["game_object"] = GetHierarchyPath(go),
                                    ["component"] = component.GetType().FullName,
                                    ["property_path"] = iterator.propertyPath,
                                    ["instance_id"] = go.GetInstanceID()
                                });
                            }
                        }
                    }
                }
            }

            return new JObject
            {
                ["active_scene"] = new JObject
                {
                    ["name"] = activeScene.name,
                    ["path"] = activeScene.path,
                    ["is_dirty"] = activeScene.isDirty,
                    ["is_loaded"] = activeScene.isLoaded,
                    ["root_count"] = activeScene.rootCount
                },
                ["object_count"] = objects.Count,
                ["missing_script_count"] = missingScriptCount,
                ["missing_reference_count"] = missingReferenceCount,
                ["missing_scripts"] = missingScripts,
                ["missing_references"] = missingReferences,
                ["truncated"] = missingScripts.Count >= limit || missingReferences.Count >= limit
            };
        }

        private static JObject BuildSettingsHealth(int limit)
        {
            var scenes = new JArray();
            int enabledCount = 0;
            int missingCount = 0;

            foreach (var scene in EditorBuildSettings.scenes.Take(limit))
            {
                bool exists = !string.IsNullOrEmpty(scene.path) && File.Exists(AssetPathToFullPath(scene.path));
                if (scene.enabled) enabledCount++;
                if (!exists) missingCount++;

                scenes.Add(new JObject
                {
                    ["path"] = scene.path,
                    ["enabled"] = scene.enabled,
                    ["exists"] = exists
                });
            }

            return new JObject
            {
                ["total"] = EditorBuildSettings.scenes.Length,
                ["enabled"] = enabledCount,
                ["missing_files"] = missingCount,
                ["returned"] = scenes.Count,
                ["scenes"] = scenes
            };
        }

        private static JObject BuildAsmdefHealth(int limit)
        {
            var issues = new JArray();
            int total = 0;
            string[] guids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            foreach (string guid in guids)
            {
                total++;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string dir = Path.GetDirectoryName(path);
                bool hasScripts = !string.IsNullOrEmpty(dir) &&
                                  Directory.Exists(AssetPathToFullPath(dir)) &&
                                  Directory.EnumerateFiles(AssetPathToFullPath(dir), "*.cs", SearchOption.AllDirectories).Any();

                if (!hasScripts && issues.Count < limit)
                {
                    issues.Add(new JObject
                    {
                        ["path"] = path,
                        ["issue"] = "asmdef 目录下没有 C# 脚本，Unity 可能报告该程序集不会编译。"
                    });
                }
            }

            return new JObject
            {
                ["total"] = total,
                ["issue_count"] = issues.Count,
                ["issues"] = issues,
                ["truncated"] = issues.Count >= limit
            };
        }

        private static JObject BuildMcpHealth()
        {
            var settings = McpProjectSettings.Load();
            bool sourceOk = AssetPathUtility.TryGetPrivateServerSource(out var source, out var sourceError);
            bool serverRunning = MCPServiceLocator.Server.IsLocalHttpServerRunning();
            bool sessionConnected = MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http);

            return new JObject
            {
                ["use_http_transport"] = settings.UseHttpTransport,
                ["http_transport_scope"] = settings.HttpTransportScope,
                ["http_base_url"] = settings.HttpBaseUrl,
                ["central_server_enabled"] = settings.CentralServerEnabled,
                ["central_server_auto_start"] = settings.CentralServerAutoStart,
                ["auto_connect"] = settings.AutoConnect,
                ["server_source_mode"] = settings.ServerSourceMode,
                ["server_source"] = source,
                ["server_source_ok"] = sourceOk,
                ["server_source_error"] = sourceError,
                ["local_server_running"] = serverRunning,
                ["session_connected"] = sessionConnected
            };
        }

        private static JObject BuildAssetHealth(string searchRoot, int limit, int maxScanFiles)
        {
            var missingScriptAssets = new JArray();
            int scanned = 0;
            int totalIssues = 0;
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".prefab", ".unity", ".asset" };

            foreach (var candidate in EnumerateTextAssets(searchRoot, extensions, maxScanFiles))
            {
                scanned++;
                string text;
                try { text = File.ReadAllText(candidate.FullPath); }
                catch { continue; }

                if (!text.Contains("m_Script: {fileID: 0}", StringComparison.Ordinal))
                {
                    continue;
                }

                totalIssues++;
                if (missingScriptAssets.Count < limit)
                {
                    missingScriptAssets.Add(new JObject
                    {
                        ["path"] = candidate.AssetPath,
                        ["type"] = Path.GetExtension(candidate.AssetPath).TrimStart('.')
                    });
                }
            }

            return new JObject
            {
                ["search_root"] = searchRoot,
                ["scan_limit"] = maxScanFiles,
                ["scan_limit_reached"] = scanned >= maxScanFiles,
                ["scanned"] = scanned,
                ["missing_script_asset_count"] = totalIssues,
                ["returned"] = missingScriptAssets.Count,
                ["truncated"] = totalIssues > missingScriptAssets.Count || scanned >= maxScanFiles,
                ["missing_script_assets"] = missingScriptAssets
            };
        }

        private static JObject BuildSummary(JObject report)
        {
            var issues = new JArray();

            var scene = report["scene"] as JObject;
            int missingScripts = scene?["missing_script_count"]?.ToObject<int>() ?? 0;
            int missingRefs = scene?["missing_reference_count"]?.ToObject<int>() ?? 0;
            if (missingScripts > 0) issues.Add($"{missingScripts} 个场景 Missing Script");
            if (missingRefs > 0) issues.Add($"{missingRefs} 个场景 Missing Reference");

            var build = report["build_settings"] as JObject;
            int missingBuildScenes = build?["missing_files"]?.ToObject<int>() ?? 0;
            if (missingBuildScenes > 0) issues.Add($"{missingBuildScenes} 个 Build Settings 场景文件不存在");

            var asmdefs = report["asmdefs"] as JObject;
            int asmdefIssues = asmdefs?["issue_count"]?.ToObject<int>() ?? 0;
            if (asmdefIssues > 0) issues.Add($"{asmdefIssues} 个 asmdef 目录没有脚本");

            var mcp = report["unity_mcp"] as JObject;
            if (mcp != null)
            {
                if (mcp["server_source_ok"]?.ToObject<bool>() == false) issues.Add("UnityMCP 本地 Server 源无效");
                if (mcp["central_server_enabled"]?.ToObject<bool>() == true &&
                    mcp["local_server_running"]?.ToObject<bool>() == false) issues.Add("UnityMCP 中心 Server 当前未运行");
                if (mcp["auto_connect"]?.ToObject<bool>() == true &&
                    mcp["session_connected"]?.ToObject<bool>() == false) issues.Add("UnityMCP AutoConnect 开启但 Session 未连接");
            }

            var assetScan = report["asset_scan"] as JObject;
            int assetIssues = assetScan?["missing_script_asset_count"]?.ToObject<int>() ?? 0;
            if (assetIssues > 0) issues.Add($"{assetIssues} 个资源文件包含 Missing Script 标记");

            return new JObject
            {
                ["status"] = issues.Count == 0 ? "ok" : "attention",
                ["issue_count"] = issues.Count,
                ["issues"] = issues
            };
        }

        private static IEnumerable<GameObject> EnumerateSceneObjects(bool includeInactive)
        {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive))
                    {
                        yield return t.gameObject;
                    }
                }
            }
        }

        private static IEnumerable<(string AssetPath, string FullPath)> EnumerateTextAssets(string searchRoot, HashSet<string> extensions, int maxFiles)
        {
            string fullRoot = AssetPathToFullPath(searchRoot);
            if (!IsInsideProject(fullRoot))
            {
                McpLog.Warn($"[project_health_check] search_root is outside the project and was skipped: {searchRoot}");
                yield break;
            }

            if (!Directory.Exists(fullRoot)) yield break;
            int yielded = 0;
            foreach (string file in Directory.EnumerateFiles(fullRoot, "*.*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (!extensions.Contains(Path.GetExtension(file))) continue;
                if (yielded >= maxFiles) yield break;
                yielded++;
                yield return (FullPathToAssetPath(file), file);
            }
        }

        private static int GetMaxScanFiles(JObject @params)
        {
            int value = @params?["max_scan_files"]?.ToObject<int?>()
                ?? @params?["maxScanFiles"]?.ToObject<int?>()
                ?? DefaultMaxScanFiles;
            return Mathf.Clamp(value, 1, MaxScanFiles);
        }

        private static string GetString(JObject @params, string snakeName, string camelName)
        {
            return @params?[snakeName]?.ToString() ?? @params?[camelName]?.ToString();
        }

        private static string NormalizeSearchRoot(string searchRoot)
        {
            return string.IsNullOrWhiteSpace(searchRoot)
                ? "Assets"
                : searchRoot.Replace('\\', '/').Trim().TrimEnd('/');
        }

        private static string AssetPathToFullPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static bool IsInsideProject(string fullPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string normalizedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedPath = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string FullPathToAssetPath(string fullPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
        }

        private static string GetHierarchyPath(GameObject go)
        {
            var names = new Stack<string>();
            var current = go.transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names);
        }
    }
}
