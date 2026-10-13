using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Read-only runtime/editor-state inspection for VibeCoding validation.
    /// </summary>
    [McpForUnityTool("inspect_runtime", AutoRegister = true)]
    public static class InspectRuntime
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 300;

        public sealed class Parameters
        {
            [ToolParameter("Action: summary, object, components, cameras, animators, canvases, or selection.")]
            public string action { get; set; }

            [ToolParameter("Target GameObject name, hierarchy path, or instance ID.", Required = false)]
            public string target { get; set; }

            [ToolParameter("Search method: by_id, by_name, by_path, by_tag, by_layer, by_component, or auto.", Required = false, DefaultValue = "auto")]
            public string search_method { get; set; }

            [ToolParameter("Include inactive GameObjects.", Required = false, DefaultValue = "true")]
            public bool? include_inactive { get; set; }

            [ToolParameter("Maximum item count. Defaults to 50, capped at 300.", Required = false, DefaultValue = "50")]
            public int? limit { get; set; }

            [ToolParameter("Include compact serialized field summaries for components.", Required = false, DefaultValue = "false")]
            public bool? include_fields { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string action = (@params?["action"]?.ToString() ?? "summary").Trim().ToLowerInvariant();
            int limit = Mathf.Clamp(@params?["limit"]?.ToObject<int?>() ?? DefaultLimit, 1, MaxLimit);
            bool includeInactive = @params?["include_inactive"]?.ToObject<bool?>()
                ?? @params?["includeInactive"]?.ToObject<bool?>()
                ?? true;

            try
            {
                return action switch
                {
                    "summary" => BuildSummary(includeInactive, limit),
                    "object" => InspectObject(@params, includeInactive, includeFields: true),
                    "components" => InspectObject(@params, includeInactive, @params?["include_fields"]?.ToObject<bool?>() ?? true),
                    "cameras" => InspectCameras(includeInactive, limit),
                    "animators" => InspectAnimators(includeInactive, limit),
                    "canvases" => InspectCanvases(includeInactive, limit),
                    "selection" => InspectSelection(limit),
                    _ => new ErrorResponse("Unknown action. Valid actions: summary, object, components, cameras, animators, canvases, selection.")
                };
            }
            catch (Exception ex)
            {
                McpLog.Error($"[inspect_runtime] {action} failed: {ex}");
                return new ErrorResponse($"inspect_runtime failed: {ex.Message}");
            }
        }

        private static object BuildSummary(bool includeInactive, int limit)
        {
            var all = EnumerateSceneObjects(includeInactive).ToList();
            var activeScene = SceneManager.GetActiveScene();

            int activeCount = all.Count(go => go.activeInHierarchy);
            int inactiveCount = all.Count - activeCount;

            return new SuccessResponse("Runtime summary completed.", new JObject
            {
                ["is_playing"] = Application.isPlaying,
                ["is_paused"] = EditorApplication.isPaused,
                ["time_scale"] = Time.timeScale,
                ["active_scene"] = new JObject
                {
                    ["name"] = activeScene.name,
                    ["path"] = activeScene.path,
                    ["is_loaded"] = activeScene.isLoaded,
                    ["is_dirty"] = activeScene.isDirty,
                    ["root_count"] = activeScene.rootCount
                },
                ["object_counts"] = new JObject
                {
                    ["total"] = all.Count,
                    ["active"] = activeCount,
                    ["inactive"] = inactiveCount
                },
                ["selection"] = BuildSelectionArray(limit),
                ["cameras"] = BuildCameraArray(includeInactive, Math.Min(limit, 20)),
                ["canvases"] = BuildCanvasArray(includeInactive, Math.Min(limit, 20)),
                ["animators"] = BuildAnimatorArray(includeInactive, Math.Min(limit, 20))
            });
        }

        private static object InspectObject(JObject @params, bool includeInactive, bool includeFields)
        {
            var go = ResolveTarget(@params, includeInactive);
            if (go == null)
            {
                return new ErrorResponse("Target GameObject not found. Provide target with search_method by_id, by_name, by_path, by_tag, by_layer, by_component, or auto.");
            }

            return new SuccessResponse("Runtime object inspection completed.", BuildGameObjectInfo(go, includeFields));
        }

        private static object InspectCameras(bool includeInactive, int limit)
        {
            return new SuccessResponse("Camera inspection completed.", new JObject
            {
                ["count"] = FindComponents<Camera>(includeInactive).Count(),
                ["items"] = BuildCameraArray(includeInactive, limit)
            });
        }

        private static object InspectAnimators(bool includeInactive, int limit)
        {
            return new SuccessResponse("Animator inspection completed.", new JObject
            {
                ["count"] = FindComponents<Animator>(includeInactive).Count(),
                ["items"] = BuildAnimatorArray(includeInactive, limit)
            });
        }

        private static object InspectCanvases(bool includeInactive, int limit)
        {
            return new SuccessResponse("Canvas inspection completed.", new JObject
            {
                ["count"] = FindComponents<Canvas>(includeInactive).Count(),
                ["items"] = BuildCanvasArray(includeInactive, limit)
            });
        }

        private static object InspectSelection(int limit)
        {
            return new SuccessResponse("Selection inspection completed.", new JObject
            {
                ["count"] = Selection.gameObjects?.Length ?? 0,
                ["items"] = BuildSelectionArray(limit)
            });
        }

        private static JObject BuildGameObjectInfo(GameObject go, bool includeFields)
        {
            var components = new JArray();
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null)
                {
                    components.Add(new JObject
                    {
                        ["type"] = "Missing Script",
                        ["missing"] = true
                    });
                    continue;
                }

                var componentInfo = new JObject
                {
                    ["type"] = component.GetType().FullName,
                    ["enabled"] = GetEnabledValue(component),
                    ["asset_path"] = GetComponentScriptPath(component)
                };

                if (includeFields)
                {
                    componentInfo["fields"] = BuildFieldSummary(component);
                }

                components.Add(componentInfo);
            }

            return new JObject
            {
                ["name"] = go.name,
                ["path"] = GetHierarchyPath(go),
                ["instance_id"] = go.GetInstanceID(),
                ["tag"] = go.tag,
                ["layer"] = LayerMask.LayerToName(go.layer),
                ["active_self"] = go.activeSelf,
                ["active_in_hierarchy"] = go.activeInHierarchy,
                ["scene"] = go.scene.path,
                ["transform"] = new JObject
                {
                    ["position"] = VectorToArray(go.transform.position),
                    ["rotation_euler"] = VectorToArray(go.transform.rotation.eulerAngles),
                    ["local_scale"] = VectorToArray(go.transform.localScale),
                    ["child_count"] = go.transform.childCount
                },
                ["components"] = components
            };
        }

        private static JArray BuildCameraArray(bool includeInactive, int limit)
        {
            var items = new JArray();
            foreach (var cam in FindComponents<Camera>(includeInactive).Take(limit))
            {
                items.Add(new JObject
                {
                    ["game_object"] = GetHierarchyPath(cam.gameObject),
                    ["instance_id"] = cam.gameObject.GetInstanceID(),
                    ["enabled"] = cam.enabled,
                    ["active"] = cam.gameObject.activeInHierarchy,
                    ["tag"] = cam.gameObject.tag,
                    ["depth"] = cam.depth,
                    ["clear_flags"] = cam.clearFlags.ToString(),
                    ["field_of_view"] = cam.fieldOfView,
                    ["near_clip"] = cam.nearClipPlane,
                    ["far_clip"] = cam.farClipPlane
                });
            }

            return items;
        }

        private static JArray BuildCanvasArray(bool includeInactive, int limit)
        {
            var items = new JArray();
            foreach (var canvas in FindComponents<Canvas>(includeInactive).Take(limit))
            {
                items.Add(new JObject
                {
                    ["game_object"] = GetHierarchyPath(canvas.gameObject),
                    ["instance_id"] = canvas.gameObject.GetInstanceID(),
                    ["enabled"] = canvas.enabled,
                    ["active"] = canvas.gameObject.activeInHierarchy,
                    ["render_mode"] = canvas.renderMode.ToString(),
                    ["sorting_layer"] = canvas.sortingLayerName,
                    ["sorting_order"] = canvas.sortingOrder,
                    ["override_sorting"] = canvas.overrideSorting
                });
            }

            return items;
        }

        private static JArray BuildAnimatorArray(bool includeInactive, int limit)
        {
            var items = new JArray();
            foreach (var animator in FindComponents<Animator>(includeInactive).Take(limit))
            {
                var controller = animator.runtimeAnimatorController;
                var obj = new JObject
                {
                    ["game_object"] = GetHierarchyPath(animator.gameObject),
                    ["instance_id"] = animator.gameObject.GetInstanceID(),
                    ["enabled"] = animator.enabled,
                    ["active"] = animator.gameObject.activeInHierarchy,
                    ["controller"] = controller != null ? AssetDatabase.GetAssetPath(controller) : null,
                    ["avatar"] = animator.avatar != null ? animator.avatar.name : null,
                    ["apply_root_motion"] = animator.applyRootMotion,
                    ["parameter_count"] = GetAnimatorParameterCount(controller)
                };

                if (controller != null && Application.isPlaying && animator.isActiveAndEnabled && animator.layerCount > 0)
                {
                    var state = animator.GetCurrentAnimatorStateInfo(0);
                    obj["layer0_state_hash"] = state.fullPathHash;
                    obj["layer0_normalized_time"] = state.normalizedTime;
                }

                items.Add(obj);
            }

            return items;
        }

        private static JArray BuildSelectionArray(int limit)
        {
            var items = new JArray();
            foreach (var go in (Selection.gameObjects ?? Array.Empty<GameObject>()).Take(limit))
            {
                if (go == null) continue;
                items.Add(new JObject
                {
                    ["name"] = go.name,
                    ["path"] = GetHierarchyPath(go),
                    ["instance_id"] = go.GetInstanceID(),
                    ["asset_path"] = AssetDatabase.GetAssetPath(go)
                });
            }

            return items;
        }

        private static JArray BuildFieldSummary(Component component)
        {
            var fields = new JArray();
            var type = component.GetType();
            var members = type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.IsPublic || f.GetCustomAttribute<SerializeField>() != null)
                .Where(f => !f.IsStatic)
                .Take(30);

            foreach (var field in members)
            {
                object value;
                try { value = field.GetValue(component); }
                catch { continue; }

                fields.Add(new JObject
                {
                    ["name"] = field.Name,
                    ["type"] = field.FieldType.Name,
                    ["value"] = FormatValue(value)
                });
            }

            return fields;
        }

        private static GameObject ResolveTarget(JObject @params, bool includeInactive)
        {
            string target = @params?["target"]?.ToString();
            string method = (@params?["search_method"] ?? @params?["searchMethod"])?.ToString();
            method = string.IsNullOrWhiteSpace(method) ? "auto" : method.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(target))
            {
                return Selection.activeGameObject;
            }

            if (method == "auto")
            {
                if (int.TryParse(target, out _)) method = "by_id";
                else if (target.Contains("/")) method = "by_path";
                else method = "by_name";
            }

            var all = EnumerateSceneObjects(includeInactive);
            return method switch
            {
                "by_id" => int.TryParse(target, out int id) ? all.FirstOrDefault(go => go.GetInstanceID() == id) : null,
                "by_path" => all.FirstOrDefault(go => string.Equals(GetHierarchyPath(go), target, StringComparison.OrdinalIgnoreCase)),
                "by_tag" => ResolveByTag(all, target),
                "by_layer" => ResolveByLayer(all, target),
                "by_component" => ResolveByComponent(all, target),
                _ => all.FirstOrDefault(go => string.Equals(go.name, target, StringComparison.OrdinalIgnoreCase))
            };
        }

        private static GameObject ResolveByTag(IEnumerable<GameObject> all, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag) ||
                !InternalEditorUtility.tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            return all.FirstOrDefault(go => go.CompareTag(tag));
        }

        private static GameObject ResolveByLayer(IEnumerable<GameObject> all, string layer)
        {
            int layerIndex = int.TryParse(layer, out int parsed) ? parsed : LayerMask.NameToLayer(layer);
            return layerIndex >= 0 ? all.FirstOrDefault(go => go.layer == layerIndex) : null;
        }

        private static GameObject ResolveByComponent(IEnumerable<GameObject> all, string componentName)
        {
            if (!ComponentResolver.TryResolve(componentName, out Type type, out _))
            {
                return null;
            }

            return all.FirstOrDefault(go => go.GetComponent(type) != null);
        }

        private static int GetAnimatorParameterCount(RuntimeAnimatorController controller)
        {
            return controller switch
            {
                UnityEditor.Animations.AnimatorController animatorController => animatorController.parameters?.Length ?? 0,
                AnimatorOverrideController overrideController => GetAnimatorParameterCount(overrideController.runtimeAnimatorController),
                _ => 0
            };
        }

        private static IEnumerable<T> FindComponents<T>(bool includeInactive) where T : Component
        {
            return EnumerateSceneObjects(includeInactive)
                .Select(go => go.GetComponent<T>())
                .Where(component => component != null);
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

        private static JArray VectorToArray(Vector3 vector)
        {
            return new JArray(vector.x, vector.y, vector.z);
        }

        private static string GetEnabledValue(Component component)
        {
            var property = component.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.PropertyType == typeof(bool))
            {
                try { return ((bool)property.GetValue(component)).ToString(); }
                catch { }
            }

            return null;
        }

        private static string GetComponentScriptPath(Component component)
        {
            if (component is not MonoBehaviour behaviour)
            {
                return null;
            }

            var script = MonoScript.FromMonoBehaviour(behaviour);
            return script != null ? AssetDatabase.GetAssetPath(script) : null;
        }

        private static string FormatValue(object value)
        {
            if (value == null) return null;
            if (value is UnityEngine.Object obj) return $"{obj.name} ({obj.GetType().Name})";
            if (value is Vector2 v2) return $"({v2.x}, {v2.y})";
            if (value is Vector3 v3) return $"({v3.x}, {v3.y}, {v3.z})";
            if (value is Color color) return $"RGBA({color.r}, {color.g}, {color.b}, {color.a})";
            if (value is string s) return s.Length > 200 ? s.Substring(0, 200) + "..." : s;
            if (value.GetType().IsPrimitive || value is decimal || value is Enum) return value.ToString();
            return value.GetType().Name;
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
