using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Read-only reference and missing-binding diagnostics for VibeCoding workflows.
    /// </summary>
    [McpForUnityTool("inspect_references", AutoRegister = true)]
    public static class InspectReferences
    {
        private const int DefaultLimit = 100;
        private const int MaxLimit = 500;
        private const int DefaultMaxScanFiles = 2000;
        private const int MaxScanFiles = 20000;

        public sealed class Parameters
        {
            [ToolParameter("Action: find_references, missing_scripts, missing_references, or summary.")]
            public string action { get; set; }

            [ToolParameter("Asset path such as Assets/Prefabs/Hero.prefab. Used by find_references.", Required = false)]
            public string path { get; set; }

            [ToolParameter("Asset GUID. Used by find_references when path is not provided.", Required = false)]
            public string guid { get; set; }

            [ToolParameter("Search folder. Defaults to Assets.", Required = false, DefaultValue = "Assets")]
            public string search_root { get; set; }

            [ToolParameter("Comma-separated asset extensions to scan. Defaults to prefab,unity,asset,mat,controller,playable.", Required = false)]
            public string extensions { get; set; }

            [ToolParameter("Maximum result count. Defaults to 100, capped at 500.", Required = false, DefaultValue = "100")]
            public int? limit { get; set; }

            [ToolParameter("Maximum text asset files to scan. Defaults to 2000, capped at 20000.", Required = false, DefaultValue = "2000")]
            public int? max_scan_files { get; set; }

            [ToolParameter("Include inactive scene GameObjects when scanning the open scene.", Required = false, DefaultValue = "true")]
            public bool? include_inactive { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string action = (@params?["action"]?.ToString() ?? "summary").Trim().ToLowerInvariant();
            int limit = Mathf.Clamp(@params?["limit"]?.ToObject<int?>() ?? DefaultLimit, 1, MaxLimit);
            int maxScanFiles = GetMaxScanFiles(@params);

            try
            {
                return action switch
                {
                    "find_references" => FindReferences(@params, limit, maxScanFiles),
                    "missing_scripts" => FindMissingScripts(@params, limit, maxScanFiles),
                    "missing_references" => FindMissingReferences(@params, limit),
                    "summary" => BuildSummary(@params, limit),
                    _ => new ErrorResponse("Unknown action. Valid actions: find_references, missing_scripts, missing_references, summary.")
                };
            }
            catch (Exception ex)
            {
                McpLog.Error($"[inspect_references] {action} failed: {ex}");
                return new ErrorResponse($"inspect_references failed: {ex.Message}");
            }
        }

        private static object FindReferences(JObject @params, int limit, int maxScanFiles)
        {
            string path = NormalizeAssetPath(@params?["path"]?.ToString());
            string guid = @params?["guid"]?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(guid))
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return new ErrorResponse("find_references requires either path or guid.");
                }

                guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrWhiteSpace(guid))
                {
                    return new ErrorResponse($"Asset not found or has no GUID: {path}");
                }
            }

            string searchRoot = NormalizeSearchRoot(GetString(@params, "search_root", "searchRoot"));
            var extensions = ParseExtensions(@params?["extensions"]?.ToString());
            var matches = new JArray();
            int scanned = 0;
            int totalMatches = 0;

            foreach (var candidate in EnumerateTextAssets(searchRoot, extensions, maxScanFiles))
            {
                scanned++;
                string text;
                try
                {
                    text = File.ReadAllText(candidate.FullPath);
                }
                catch
                {
                    continue;
                }

                if (text.IndexOf(guid, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                totalMatches++;
                if (matches.Count < limit)
                {
                    matches.Add(new JObject
                    {
                        ["path"] = candidate.AssetPath,
                        ["type"] = Path.GetExtension(candidate.AssetPath).TrimStart('.'),
                        ["asset_guid"] = AssetDatabase.AssetPathToGUID(candidate.AssetPath)
                    });
                }
            }

            return new SuccessResponse("Reference scan completed.", new JObject
            {
                ["target_path"] = path,
                ["target_guid"] = guid,
                ["search_root"] = searchRoot,
                ["scan_limit"] = maxScanFiles,
                ["scan_limit_reached"] = scanned >= maxScanFiles,
                ["scanned"] = scanned,
                ["total_matches"] = totalMatches,
                ["returned"] = matches.Count,
                ["truncated"] = totalMatches > matches.Count || scanned >= maxScanFiles,
                ["references"] = matches
            });
        }

        private static object FindMissingScripts(JObject @params, int limit, int maxScanFiles)
        {
            string searchRoot = NormalizeSearchRoot(GetString(@params, "search_root", "searchRoot"));
            bool scanAssets = !string.Equals(searchRoot, "scene", StringComparison.OrdinalIgnoreCase);
            var sceneResults = FindMissingScriptsInOpenScenes(limit);
            int assetScanned = 0;
            int assetMarkerCount = 0;
            var assetResults = scanAssets
                ? FindMissingScriptMarkersInAssets(searchRoot, limit - sceneResults.Count, maxScanFiles, out assetScanned, out assetMarkerCount)
                : new JArray();
            if (!scanAssets)
            {
                assetScanned = 0;
                assetMarkerCount = 0;
            }
            int total = sceneResults.Count + assetResults.Count;

            return new SuccessResponse("Missing script scan completed.", new JObject
            {
                ["search_root"] = searchRoot,
                ["asset_scanned"] = assetScanned,
                ["asset_scan_limit"] = maxScanFiles,
                ["asset_scan_limit_reached"] = assetScanned >= maxScanFiles,
                ["asset_missing_script_marker_count"] = assetMarkerCount,
                ["returned"] = total,
                ["truncated"] = total >= limit || assetScanned >= maxScanFiles,
                ["scene_missing_scripts"] = sceneResults,
                ["asset_missing_script_markers"] = assetResults
            });
        }

        private static object FindMissingReferences(JObject @params, int limit)
        {
            bool includeInactive = @params?["include_inactive"]?.ToObject<bool?>()
                ?? @params?["includeInactive"]?.ToObject<bool?>()
                ?? true;
            var results = new JArray();
            int total = 0;

            foreach (var go in EnumerateSceneObjects(includeInactive))
            {
                var components = go.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component == null)
                    {
                        continue;
                    }

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

                        if (iterator.objectReferenceValue != null || iterator.objectReferenceInstanceIDValue == 0)
                        {
                            continue;
                        }

                        total++;
                        if (results.Count < limit)
                        {
                            results.Add(new JObject
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

            return new SuccessResponse("Missing reference scan completed.", new JObject
            {
                ["scene"] = SceneManager.GetActiveScene().path,
                ["total_missing_references"] = total,
                ["returned"] = results.Count,
                ["truncated"] = total > results.Count,
                ["missing_references"] = results
            });
        }

        private static object BuildSummary(JObject @params, int limit)
        {
            var missingScripts = FindMissingScriptsInOpenScenes(Math.Min(limit, 50));
            var missingRefs = ((SuccessResponse)FindMissingReferences(@params, Math.Min(limit, 50))).Data as JObject;

            return new SuccessResponse("Reference summary completed.", new JObject
            {
                ["active_scene"] = SceneManager.GetActiveScene().path,
                ["scene_missing_scripts_returned"] = missingScripts.Count,
                ["scene_missing_scripts"] = missingScripts,
                ["missing_references_returned"] = missingRefs?["returned"] ?? 0,
                ["missing_references"] = missingRefs?["missing_references"] ?? new JArray()
            });
        }

        private static JArray FindMissingScriptsInOpenScenes(int limit)
        {
            var results = new JArray();
            foreach (var go in EnumerateSceneObjects(includeInactive: true))
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (missing <= 0)
                {
                    continue;
                }

                results.Add(new JObject
                {
                    ["game_object"] = GetHierarchyPath(go),
                    ["instance_id"] = go.GetInstanceID(),
                    ["missing_count"] = missing
                });

                if (results.Count >= limit)
                {
                    break;
                }
            }

            return results;
        }

        private static JArray FindMissingScriptMarkersInAssets(string searchRoot, int limit, int maxScanFiles, out int scanned, out int markerCount)
        {
            var results = new JArray();
            scanned = 0;
            markerCount = 0;
            if (limit <= 0)
            {
                return results;
            }

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

                markerCount++;
                results.Add(new JObject
                {
                    ["path"] = candidate.AssetPath,
                    ["type"] = Path.GetExtension(candidate.AssetPath).TrimStart('.')
                });

                if (results.Count >= limit)
                {
                    break;
                }
            }

            return results;
        }

        private static IEnumerable<GameObject> EnumerateSceneObjects(bool includeInactive)
        {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                {
                    continue;
                }

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
                McpLog.Warn($"[inspect_references] search_root is outside the project and was skipped: {searchRoot}");
                yield break;
            }

            if (!Directory.Exists(fullRoot))
            {
                yield break;
            }

            int yielded = 0;
            foreach (string file in Directory.EnumerateFiles(fullRoot, "*.*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string ext = Path.GetExtension(file);
                if (!extensions.Contains(ext))
                {
                    continue;
                }

                if (yielded >= maxFiles)
                {
                    yield break;
                }

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

        private static HashSet<string> ParseExtensions(string extensions)
        {
            var defaults = new[] { ".prefab", ".unity", ".asset", ".mat", ".controller", ".playable" };
            var values = string.IsNullOrWhiteSpace(extensions)
                ? defaults
                : extensions.Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrWhiteSpace(e));

            return values
                .Select(e => e.StartsWith(".") ? e : "." + e)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return path.Replace('\\', '/').Trim();
        }

        private static string NormalizeSearchRoot(string searchRoot)
        {
            if (string.IsNullOrWhiteSpace(searchRoot))
            {
                return "Assets";
            }

            return searchRoot.Replace('\\', '/').Trim().TrimEnd('/');
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
            string relative = Path.GetRelativePath(projectRoot, fullPath);
            return relative.Replace('\\', '/');
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
