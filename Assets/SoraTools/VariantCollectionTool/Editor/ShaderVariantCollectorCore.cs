using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Debug = UnityEngine.Debug;

namespace ShaderVariantCollectorTool
{
    /// <summary>
    /// Shader变体收集核心类
    /// 负责实际的收集逻辑
    /// </summary>
    public class ShaderVariantCollectorCore
    {
        /// <summary>
        /// 收集状态枚举
        /// </summary>
        private enum CollectionState
        {
            None,
            Prepare,
            ScanMaterials,
            CollectVariants,
            BatchSleeping,
            WaitingComplete,
            Done
        }

        // 等待时间配置
        private const float MinWaitMilliseconds = 100f;      // 最小等待时间
        private const float MaxWaitMilliseconds = 3000f;     // 最大等待时间
        private const float SampleIntervalMilliseconds = 50f; // 采样间隔
        private const int StableCheckCount = 3;              // 稳定检查次数（连续N次采样结果相同则认为稳定）

        // 批次间隔配置
        private const float MinBatchSleepMilliseconds = 50f;  // 最小批次间隔
        private const float MaxBatchSleepMilliseconds = 500f; // 最大批次间隔
        private const float SleepPerMaterial = 10f;           // 每个材质增加的等待时间(ms)

        // 收集参数
        private string _materialFolder;
        private string _savePath;
        private int _batchSize;
        private bool _exportJson;
        private bool _includePrefabs; // 是否扫描预制体材质
        private List<string> _scenePaths; // 场景模式使用
        private string _outputFolder; // 场景模式输出文件夹
        private Action<float, string> _progressCallback;
        private Action<bool, string> _completeCallback;

        // 运行时数据
        private CollectionState _currentState = CollectionState.None;
        private Stopwatch _elapsedTime;
        private List<string> _allMaterials;
        private List<GameObject> _allSpheres = new List<GameObject>(1000);
        private int _totalMaterialCount;
        private int _processedMaterialCount;
        private string _originalScenePath; // 保存原始场景路径

        // 场景模式运行时数据
        private int _currentSceneIndex;
        private int _totalSceneCount;
        private List<string> _generatedVariantFiles = new List<string>(); // 已生成的变体文件列表

        // 动态等待相关
        private int _lastVariantCount;
        private int _stableCount;
        private int _currentBatchMaterialCount;
        private long _lastSampleTimeMilliseconds; // 上次采样的时间戳

        /// <summary>
        /// 开始收集Shader变体
        /// </summary>
        public void StartCollection(string materialFolder, string savePath, int batchSize, bool exportJson, bool includePrefabs,
            Action<float, string> progressCallback, Action<bool, string> completeCallback)
        {
            if (_currentState != CollectionState.None)
            {
                Debug.LogWarning("Collection is already running!");
                return;
            }

            // 验证参数
            if (string.IsNullOrEmpty(materialFolder))
            {
                completeCallback?.Invoke(false, "Material folder path is empty!");
                return;
            }

            if (string.IsNullOrEmpty(savePath))
            {
                completeCallback?.Invoke(false, "Save path is empty!");
                return;
            }

            // 确保文件扩展名正确
            if (!savePath.EndsWith(".shadervariants"))
            {
                if (Path.HasExtension(savePath))
                {
                    completeCallback?.Invoke(false, "Save path must have .shadervariants extension!");
                    return;
                }
                savePath = savePath + ".shadervariants";
            }

            // 保存原始场景路径（整个收集任务只保存一次，不在OnPrepare中覆盖）
            _originalScenePath = EditorSceneManager.GetActiveScene().path;

            // 若当前场景从未保存（路径为空），提前告知用户无法恢复
            if (string.IsNullOrEmpty(_originalScenePath))
            {
                if (!EditorUtility.DisplayDialog("Scene Not Saved / 场景未保存",
                    "Your current scene has not been saved yet.\n" +
                    "After collection finishes, the editor will remain in a blank temporary scene " +
                    "rather than returning to your current scene.\n\n" +
                    "It is strongly recommended to save your scene first (File → Save Scene).\n\n" +
                    "Continue anyway?",
                    "Continue", "Cancel"))
                {
                    return;
                }
            }

            // 保存参数
            _materialFolder = NormalizePath(materialFolder);
            _savePath = NormalizePath(savePath);
            _batchSize = Mathf.Max(1, batchSize);
            _exportJson = exportJson;
            _includePrefabs = includePrefabs;
            _progressCallback = progressCallback;
            _completeCallback = completeCallback;

            // 删除旧文件
            AssetDatabase.DeleteAsset(_savePath);
            EnsureDirectoryExistsUnity(_savePath);

            // 聚焦游戏窗口（只在收集开始时聚焦一次）
            FocusGameWindow();

            // 开始收集
            _currentState = CollectionState.Prepare;
            EditorApplication.update += UpdateCollection;

            ReportProgress(0f, "Starting collection...");
        }

        /// <summary>
        /// 按场景批量收集Shader变体
        /// </summary>
        public void StartCollectionByScenes(List<string> scenePaths, string outputFolder, int batchSize, bool exportJson,
            Action<float, string> progressCallback, Action<bool, string> completeCallback)
        {
            if (_currentState != CollectionState.None)
            {
                Debug.LogWarning("Collection is already running!");
                return;
            }

            // 验证参数
            if (scenePaths == null || scenePaths.Count == 0)
            {
                completeCallback?.Invoke(false, "No scenes to process!");
                return;
            }

            if (string.IsNullOrEmpty(outputFolder))
            {
                completeCallback?.Invoke(false, "Output folder path is empty!");
                return;
            }

            // 验证输出文件夹路径
            if (!outputFolder.StartsWith("Assets/"))
            {
                Debug.LogError($"[Scene Mode] Invalid output folder path: {outputFolder}. Must start with 'Assets/'");
                completeCallback?.Invoke(false, $"Invalid output folder path: {outputFolder}. Must be a valid Unity project path starting with 'Assets/'");
                return;
            }

            // 保存参数
            _scenePaths = new List<string>(scenePaths);
            _outputFolder = NormalizePath(outputFolder);
            _batchSize = Mathf.Max(1, batchSize);
            _exportJson = exportJson;
            _progressCallback = progressCallback;
            _completeCallback = completeCallback;

            // 初始化场景模式数据
            _currentSceneIndex = 0;
            _totalSceneCount = _scenePaths.Count;
            _generatedVariantFiles.Clear(); // 清空已生成文件列表

            // 保存原始场景路径（整个收集任务只保存一次，ProcessNextScene/OnPrepare不覆盖）
            _originalScenePath = EditorSceneManager.GetActiveScene().path;

            // 若当前场景从未保存（路径为空），提前告知用户无法恢复
            if (string.IsNullOrEmpty(_originalScenePath))
            {
                if (!EditorUtility.DisplayDialog("Scene Not Saved / 场景未保存",
                    "Your current scene has not been saved yet.\n" +
                    "After collection finishes, the editor will remain in a blank temporary scene " +
                    "rather than returning to your current scene.\n\n" +
                    "It is strongly recommended to save your scene first (File → Save Scene).\n\n" +
                    "Continue anyway?",
                    "Continue", "Cancel"))
                {
                    return;
                }
            }

            Debug.Log($"[Scene Mode] Starting collection for {_totalSceneCount} scenes");
            Debug.Log($"[Scene Mode] Output folder: {_outputFolder}");

            // 确保输出目录存在
            if (!AssetDatabase.IsValidFolder(_outputFolder))
            {
                // 创建文件夹
                string parentFolder = NormalizePath(Path.GetDirectoryName(_outputFolder));
                string folderName = Path.GetFileName(_outputFolder);

                if (!AssetDatabase.IsValidFolder(parentFolder))
                {
                    Debug.LogError($"[Scene Mode] Parent folder does not exist: {parentFolder}");
                    completeCallback?.Invoke(false, $"Parent folder does not exist: {parentFolder}");
                    return;
                }

                string guid = AssetDatabase.CreateFolder(parentFolder, folderName);
                if (string.IsNullOrEmpty(guid))
                {
                    Debug.LogError($"[Scene Mode] Failed to create output folder: {_outputFolder}");
                    completeCallback?.Invoke(false, $"Failed to create output folder: {_outputFolder}");
                    return;
                }

                Debug.Log($"[Scene Mode] Created output folder: {_outputFolder}");
                AssetDatabase.Refresh();
            }

            // 聚焦游戏窗口（只在收集开始时聚焦一次，确保 Unity 渲染帧以捕获变体）
            FocusGameWindow();

            // 开始处理第一个场景
            ProcessNextScene();
        }

        /// <summary>
        /// 处理下一个场景
        /// </summary>
        private void ProcessNextScene()
        {
            if (_currentSceneIndex >= _totalSceneCount)
            {
                // 所有场景处理完成，生成总表
                Debug.Log($"[Scene Mode] All {_totalSceneCount} scenes processed. Generating master variant collection...");
                GenerateMasterVariantCollection();
                return;
            }

            // 获取当前场景路径
            string scenePath = _scenePaths[_currentSceneIndex];
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);

            Debug.Log($"[Scene Mode] Starting scene {_currentSceneIndex + 1}/{_totalSceneCount}: {sceneName}");
            ReportProgress((float)_currentSceneIndex / _totalSceneCount, $"Processing scene {_currentSceneIndex + 1}/{_totalSceneCount}: {sceneName}");

            // 扫描场景中的材质
            List<string> materials = ScanMaterialsInScene(scenePath);

            if (materials.Count == 0)
            {
                Debug.LogWarning($"No materials found in scene: {sceneName}. Skipping...");
                _currentSceneIndex++;
                // 使用 delayCall 而非直接递归，避免大量空场景时栈溢出
                EditorApplication.delayCall += ProcessNextScene;
                return;
            }

            // 设置保存路径
            _savePath = NormalizePath(Path.Combine(_outputFolder, $"{sceneName}.shadervariants"));
            Debug.Log($"[Scene Mode] Save path set to: {_savePath}");

            // 删除旧文件
            AssetDatabase.DeleteAsset(_savePath);

            // 准备材质文件夹路径（场景模式不需要，直接使用材质列表）
            _materialFolder = "";

            // 设置材质列表
            _allMaterials = materials;
            _totalMaterialCount = materials.Count;
            _processedMaterialCount = 0;

            Debug.Log($"[Scene Mode] Ready to collect {materials.Count} materials for scene: {sceneName}");

            // 开始收集当前场景
            // 使用幂等注册模式，防止多次注册导致状态机每帧重复执行
            _currentState = CollectionState.Prepare;
            EditorApplication.update -= UpdateCollection;
            EditorApplication.update += UpdateCollection;
        }

        /// <summary>
        /// 取消收集
        /// </summary>
        public void CancelCollection()
        {
            if (_currentState == CollectionState.None)
                return;

            Debug.Log("[Cancel] Cancelling collection...");

            // 停止更新循环
            EditorApplication.update -= UpdateCollection;

            // 清理场景中的对象
            CleanupScene();

            // 恢复原始场景
            if (!string.IsNullOrEmpty(_originalScenePath))
            {
                try
                {
                    EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
                    Debug.Log($"[Cancel] Restored original scene: {Path.GetFileNameWithoutExtension(_originalScenePath)}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Cancel] Failed to restore original scene '{_originalScenePath}': {e.Message}");
                    EditorUtility.DisplayDialog("Error",
                        $"Failed to restore original scene!\n\nScene path: {_originalScenePath}\n\nPlease manually reopen your scene.",
                        "OK");
                }
            }

            // 如果是场景模式且已生成部分文件，询问是否删除
            if (_scenePaths != null && _generatedVariantFiles.Count > 0)
            {
                bool deleteFiles = EditorUtility.DisplayDialog("Delete Generated Files?",
                    $"Collection was cancelled. {_generatedVariantFiles.Count} variant file(s) were generated before cancellation.\n\nDo you want to delete these partial files?",
                    "Delete", "Keep");

                if (deleteFiles)
                {
                    foreach (var filePath in _generatedVariantFiles)
                    {
                        try
                        {
                            AssetDatabase.DeleteAsset(filePath);
                            Debug.Log($"[Cancel] Deleted partial file: {filePath}");

                            // 同时删除 JSON 文件（如果存在）
                            string jsonPath = Path.ChangeExtension(filePath, ".json");
                            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(jsonPath) != null)
                            {
                                AssetDatabase.DeleteAsset(jsonPath);
                                Debug.Log($"[Cancel] Deleted partial JSON file: {jsonPath}");
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[Cancel] Failed to delete file {filePath}: {e.Message}");
                        }
                    }

                    AssetDatabase.Refresh();
                    Debug.Log($"[Cancel] Deleted {_generatedVariantFiles.Count} partial file(s)");
                }
                else
                {
                    Debug.Log($"[Cancel] Kept {_generatedVariantFiles.Count} partial file(s)");
                }
            }

            // 清理所有状态
            _currentState = CollectionState.None;
            _scenePaths = null;
            _allMaterials = null;
            _generatedVariantFiles.Clear();
            _originalScenePath = null;

            // 卸载未使用的资源
            Resources.UnloadUnusedAssets();

            ReportProgress(0f, "Collection cancelled by user");
            _completeCallback?.Invoke(false, "Collection cancelled by user");
        }

        /// <summary>
        /// 更新收集流程（状态机）
        /// </summary>
        private void UpdateCollection()
        {
            switch (_currentState)
            {
                case CollectionState.Prepare:
                    OnPrepare();
                    break;

                case CollectionState.ScanMaterials:
                    OnScanMaterials();
                    break;

                case CollectionState.CollectVariants:
                    OnCollectVariants();
                    break;

                case CollectionState.BatchSleeping:
                    OnBatchSleeping();
                    break;

                case CollectionState.WaitingComplete:
                    OnWaitingComplete();
                    break;

                case CollectionState.Done:
                    OnDone();
                    break;
            }
        }

        /// <summary>
        /// 准备阶段
        /// </summary>
        private void OnPrepare()
        {
            // 注意：_originalScenePath 已在 StartCollection/StartCollectionByScenes 中保存，此处不覆盖

            ShaderVariantCollectionHelper.ClearCurrentShaderVariantCollection();

            if (!CreateTempScene())
            {
                Debug.LogError("[Prepare] Failed to create temporary scene, aborting collection.");
                EditorApplication.update -= UpdateCollection;
                _currentState = CollectionState.None;
                _completeCallback?.Invoke(false, "Failed to create temporary scene. Collection aborted.");
                return;
            }

            ReportProgress(0.05f, "Preparation complete");
            _currentState = CollectionState.ScanMaterials;
        }

        /// <summary>
        /// 扫描材质阶段
        /// </summary>
        private void OnScanMaterials()
        {
            // 如果在场景模式下，材质列表已经准备好了
            if (_allMaterials == null || _allMaterials.Count == 0)
            {
                // 文件夹模式：扫描材质和预制体
                HashSet<string> allMaterialPaths = new HashSet<string>();

                // 1. 扫描材质文件
                string[] materialPaths = ScanMaterials(new string[] { _materialFolder });
                foreach (var path in materialPaths)
                {
                    allMaterialPaths.Add(path);
                }
                Debug.Log($"[Scan] Found {materialPaths.Length} material files");

                // 2. 如果启用，扫描预制体中的材质引用
                if (_includePrefabs)
                {
                    var prefabMaterials = ScanMaterialsInPrefabs(new string[] { _materialFolder });
                    int prefabMaterialCount = 0;
                    foreach (var path in prefabMaterials)
                    {
                        if (allMaterialPaths.Add(path))
                        {
                            prefabMaterialCount++;
                        }
                    }
                    Debug.Log($"[Scan] Found {prefabMaterialCount} additional materials from prefabs");
                }

                _allMaterials = new List<string>(allMaterialPaths);
                _totalMaterialCount = _allMaterials.Count;
                _processedMaterialCount = 0;
            }

            if (_totalMaterialCount == 0)
            {
                ReportProgress(1f, "No materials found!");
                _completeCallback?.Invoke(false, "No materials found in the specified folder");
                _currentState = CollectionState.Done;
                return;
            }

            ReportProgress(0.1f, $"Found {_totalMaterialCount} materials");
            _currentState = CollectionState.CollectVariants;
        }

        /// <summary>
        /// 收集变体阶段
        /// </summary>
        private void OnCollectVariants()
        {
            int count = Mathf.Min(_batchSize, _allMaterials.Count);
            List<string> batch = _allMaterials.GetRange(0, count);

            // 先执行渲染，成功后再从列表移除（防止Camera为null时静默丢失材质）
            bool ok;
            try
            {
                ok = CollectBatch(batch);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CollectVariants] CollectBatch threw an exception: {e}");
                _completeCallback?.Invoke(false, $"Collection aborted: {e.Message}");
                _currentState = CollectionState.Done;
                return;
            }
            if (!ok)
            {
                Debug.LogError("[CollectVariants] CollectBatch failed (Main Camera not found). Aborting collection.");
                _completeCallback?.Invoke(false, "Collection failed: Main Camera not found in the temporary scene. Please ensure your project uses NewSceneSetup.DefaultGameObjects.");
                _currentState = CollectionState.Done;
                return;
            }

            _allMaterials.RemoveRange(0, count);
            _processedMaterialCount += count;
            _currentBatchMaterialCount = count; // 记录当前批次材质数量

            float progress = 0.1f + 0.8f * (_processedMaterialCount / (float)_totalMaterialCount);
            ReportProgress(progress, $"Processing materials: {_processedMaterialCount}/{_totalMaterialCount}");

            if (_allMaterials.Count > 0)
            {
                // 还有更多材质，进入休眠状态
                _elapsedTime = Stopwatch.StartNew();
                _currentState = CollectionState.BatchSleeping;
            }
            else
            {
                // 所有材质处理完成，等待完成（初始化稳定性检测）
                _elapsedTime = Stopwatch.StartNew();
                _lastVariantCount = -1;
                _stableCount = 0;
                _lastSampleTimeMilliseconds = 0; // 初始化采样时间戳
                _currentState = CollectionState.WaitingComplete;
            }
        }

        /// <summary>
        /// 批次间休眠
        /// </summary>
        private void OnBatchSleeping()
        {
            // 根据当前批次的材质数量动态计算等待时间
            float dynamicSleepTime = CalculateBatchSleepTime(_currentBatchMaterialCount);

            if (_elapsedTime.ElapsedMilliseconds > dynamicSleepTime)
            {
                DestroyAllSpheres();
                _elapsedTime.Stop();
                _currentState = CollectionState.CollectVariants;
            }
        }

        /// <summary>
        /// 等待完成
        /// </summary>
        private void OnWaitingComplete()
        {
            long currentTimeMilliseconds = _elapsedTime.ElapsedMilliseconds;

            // 检查是否达到最大等待时间
            if (currentTimeMilliseconds > MaxWaitMilliseconds)
            {
                Debug.LogWarning($"[Wait Complete] Reached max wait time ({MaxWaitMilliseconds}ms), proceeding to save...");
                _elapsedTime.Stop();

                try
                {
                    SaveResults();
                    ReportProgress(1f, "Collection completed successfully!");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Wait Complete] Failed to save results: {e.Message}");
                    ReportProgress(0f, "Failed to save results!");
                    _completeCallback?.Invoke(false, $"Failed to save results: {e.Message}");
                }

                _currentState = CollectionState.Done;
                return;
            }

            // 检查是否达到最小等待时间
            if (currentTimeMilliseconds < MinWaitMilliseconds)
            {
                return; // 还没到最小等待时间，继续等待
            }

            // 检查是否到了下一次采样时间
            long timeSinceLastSample = currentTimeMilliseconds - _lastSampleTimeMilliseconds;
            if (timeSinceLastSample < SampleIntervalMilliseconds)
            {
                return; // 还没到下一次采样时间
            }

            // 更新采样时间戳
            _lastSampleTimeMilliseconds = currentTimeMilliseconds;

            // 采样当前变体数量
            int currentVariantCount = ShaderVariantCollectionHelper.GetCurrentShaderVariantCollectionVariantCount();

            if (_lastVariantCount == currentVariantCount)
            {
                // 变体数量稳定
                _stableCount++;
                Debug.Log($"[Wait Complete] Variant count stable: {currentVariantCount} (stable count: {_stableCount}/{StableCheckCount})");

                if (_stableCount >= StableCheckCount)
                {
                    // 连续多次采样都稳定，认为收集完成
                    Debug.Log($"[Wait Complete] Variant count stabilized at {currentVariantCount} after {currentTimeMilliseconds}ms");
                    _elapsedTime.Stop();

                    try
                    {
                        SaveResults();
                        ReportProgress(1f, "Collection completed successfully!");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Wait Complete] Failed to save results: {e.Message}");
                        ReportProgress(0f, "Failed to save results!");
                        _completeCallback?.Invoke(false, $"Failed to save results: {e.Message}");
                    }

                    _currentState = CollectionState.Done;
                }
            }
            else
            {
                // 变体数量还在变化，重置稳定计数
                Debug.Log($"[Wait Complete] Variant count changed: {_lastVariantCount} -> {currentVariantCount}");
                _lastVariantCount = currentVariantCount;
                _stableCount = 0;
            }
        }

        /// <summary>
        /// 完成阶段
        /// </summary>
        private void OnDone()
        {
            EditorApplication.update -= UpdateCollection;
            CleanupScene();

            int shaderCount = ShaderVariantCollectionHelper.GetCurrentShaderVariantCollectionShaderCount();
            int variantCount = ShaderVariantCollectionHelper.GetCurrentShaderVariantCollectionVariantCount();

            // 检查是否在场景模式下
            if (_scenePaths != null && _scenePaths.Count > 0)
            {
                // 场景模式：记录生成的文件，处理下一个场景
                string currentSceneName = Path.GetFileNameWithoutExtension(_scenePaths[_currentSceneIndex]);
                Debug.Log($"[Scene Mode] Completed scene {_currentSceneIndex + 1}/{_totalSceneCount}: {currentSceneName} (Shaders: {shaderCount}, Variants: {variantCount})");

                // 将生成的文件路径添加到列表
                if (!string.IsNullOrEmpty(_savePath) && AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(_savePath) != null)
                {
                    _generatedVariantFiles.Add(_savePath);
                    Debug.Log($"[Scene Mode] Added to generated files: {_savePath}");
                }

                Debug.Log($"[Scene Mode] Moving to next scene. Current index: {_currentSceneIndex}, Total: {_totalSceneCount}");

                _currentSceneIndex++;
                _currentState = CollectionState.None;

                // 继续处理下一个场景
                ProcessNextScene();
            }
            else
            {
                // 文件夹模式：完成收集，恢复原始场景
                RestoreOriginalScene();
                Debug.Log($"[Folder Mode] Collection completed! Shaders: {shaderCount}, Variants: {variantCount}");
                _completeCallback?.Invoke(true, $"Collection completed! Shaders: {shaderCount}, Variants: {variantCount}");
                _currentState = CollectionState.None;
            }
        }

        /// <summary>
        /// 创建临时场景，返回是否成功
        /// </summary>
        private bool CreateTempScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
            if (!scene.IsValid())
            {
                Debug.LogError("[CreateTempScene] NewScene returned an invalid scene.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 恢复原始场景（在整个收集任务结束时调用一次）
        /// </summary>
        private void RestoreOriginalScene()
        {
            if (string.IsNullOrEmpty(_originalScenePath))
            {
                Debug.Log("[Restore] No original scene path saved, skipping restore.");
                return;
            }
            try
            {
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
                Debug.Log($"[Restore] Restored original scene: {Path.GetFileNameWithoutExtension(_originalScenePath)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Restore] Failed to restore original scene '{_originalScenePath}': {e.Message}");
                EditorUtility.DisplayDialog("Error",
                    $"Failed to restore your original scene!\n\nScene path: {_originalScenePath}\n\nPlease manually reopen your scene.",
                    "OK");
            }
            _originalScenePath = null;
        }

        /// <summary>
        /// 扫描材质文件
        /// </summary>
        private string[] ScanMaterials(string[] folders)
        {
            // 移除末尾斜杠
            for (int i = 0; i < folders.Length; i++)
            {
                folders[i] = folders[i].TrimEnd('/', '\\');
            }

            string[] guids = AssetDatabase.FindAssets("t:Material", folders);

            // 去重
            HashSet<string> paths = new HashSet<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            return new List<string>(paths).ToArray();
        }

        /// <summary>
        /// 扫描预制体中的材质引用
        /// </summary>
        private HashSet<string> ScanMaterialsInPrefabs(string[] folders)
        {
            HashSet<string> materialPaths = new HashSet<string>();

            // 移除末尾斜杠
            for (int i = 0; i < folders.Length; i++)
            {
                folders[i] = folders[i].TrimEnd('/', '\\');
            }

            // 查找所有预制体
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", folders);
            Debug.Log($"[Prefab Scan] Found {prefabGuids.Length} prefabs to scan");

            int scannedCount = 0;
            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(prefabPath))
                    continue;

                // 加载预制体
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                    continue;

                // 扫描预制体中的所有材质
                ScanPrefabMaterials(prefab, materialPaths);
                scannedCount++;

                // 每100个预制体输出一次进度并卸载资源
                if (scannedCount % 100 == 0)
                {
                    Debug.Log($"[Prefab Scan] Scanned {scannedCount}/{prefabGuids.Length} prefabs, found {materialPaths.Count} unique materials");
                    Resources.UnloadUnusedAssets();
                }
            }

            Debug.Log($"[Prefab Scan] Completed. Scanned {scannedCount} prefabs, found {materialPaths.Count} unique materials");

            // 最后再卸载一次未使用的资源
            Resources.UnloadUnusedAssets();

            return materialPaths;
        }

        /// <summary>
        /// 扫描单个预制体中的所有材质引用
        /// </summary>
        private void ScanPrefabMaterials(GameObject prefab, HashSet<string> materialPaths)
        {
            if (prefab == null) return;

            // 直接使用CollectMaterialsFromGameObject来扫描预制体的所有子对象
            CollectMaterialsFromGameObject(prefab, materialPaths, true);
        }

        /// <summary>
        /// 收集一批材质的变体，返回 false 表示主相机不存在（整批跳过）
        /// </summary>
        private bool CollectBatch(List<string> materials)
        {
            try
            {
                Camera camera = Camera.main;
                if (camera == null)
                {
                    Debug.LogError("Main camera not found! Please ensure the temp scene has a Main Camera tagged 'MainCamera'.");
                    return false;
                }

                // 设置主相机
                float aspect = camera.aspect;
                int totalMaterials = materials.Count;
                float height = Mathf.Sqrt(totalMaterials / aspect) + 1;
                float width = Mathf.Sqrt(totalMaterials / aspect) * aspect + 1;
                float halfHeight = Mathf.CeilToInt(height / 2f);
                float halfWidth = Mathf.CeilToInt(width / 2f);
                camera.orthographic = true;
                camera.orthographicSize = halfHeight;
                camera.transform.position = new Vector3(0f, 0f, -10f);

                // 创建球体网格
                int xMax = (int)(width - 1);
                int x = 0, y = 0;

                for (int i = 0; i < materials.Count; i++)
                {
                    try
                    {
                        var material = materials[i];
                        var position = new Vector3(x - halfWidth + 1f, y - halfHeight + 1f, 0f);
                        var go = CreateSphere(material, position, i);

                        if (go != null)
                            _allSpheres.Add(go);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Collect Batch] Failed to create sphere for material at index {i}: {e.Message}");
                        // 单个材质失败不应影响整体收集，继续处理下一个
                    }

                    if (x == xMax)
                    {
                        x = 0;
                        y++;
                    }
                    else
                    {
                        x++;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Collect Batch] Critical error during batch collection: {e.Message}\n{e.StackTrace}");
                // 清理已创建的球体
                DestroyAllSpheres();
                throw; // 重新抛出异常
            }
            return true;
        }

        /// <summary>
        /// 创建带材质的球体
        /// </summary>
        private GameObject CreateSphere(string assetPath, Vector3 position, int index)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null || material.shader == null)
                return null;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                Debug.LogError($"Failed to get Renderer component from sphere at index {index}");
                GameObject.DestroyImmediate(go);
                return null;
            }

            renderer.sharedMaterial = material;
            go.transform.position = position;
            go.name = $"Sphere_{index} | {material.name}";
            return go;
        }

        /// <summary>
        /// 销毁所有球体
        /// </summary>
        private void DestroyAllSpheres()
        {
            foreach (var go in _allSpheres)
            {
                if (go != null)
                    GameObject.DestroyImmediate(go);
            }
            _allSpheres.Clear();

            // 卸载未使用的资源
            Resources.UnloadUnusedAssets();
        }

        /// <summary>
        /// 清理场景
        /// </summary>
        private void CleanupScene()
        {
            DestroyAllSpheres();
        }

        /// <summary>
        /// 保存收集结果
        /// </summary>
        private void SaveResults()
        {
            Debug.Log($"[Save] Saving shader variants to: {_savePath}");

            try
            {
                // 保存.shadervariants文件
                ShaderVariantCollectionHelper.SaveCurrentShaderVariantCollection(_savePath);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                // 生成JSON清单（如果启用）
                if (_exportJson)
                {
                    try
                    {
                        var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(_savePath);
                        if (svc != null)
                        {
                            var manifest = ShaderVariantCollectionManifest.Extract(svc);
                            if (manifest != null)
                            {
                                string jsonData = JsonUtility.ToJson(manifest, true);
                                string jsonPath = NormalizePath(Path.ChangeExtension(_savePath, ".json"));
                                WriteTextFileUnity(jsonPath, jsonData);
                                Debug.Log($"[Save] JSON manifest saved to: {jsonPath}");
                            }
                            else
                            {
                                Debug.LogWarning($"[Save] Failed to extract manifest from ShaderVariantCollection");
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[Save] Failed to load saved ShaderVariantCollection from: {_savePath}");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Save] Failed to export JSON manifest: {e.Message}\n{e.StackTrace}");
                        // JSON导出失败不应阻止整个保存流程，仅记录错误
                    }
                }

                Debug.Log($"[Save] File saved successfully: {_savePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Failed to save shader variants: {e.Message}\n{e.StackTrace}");
                throw; // 重新抛出异常，让调用者处理
            }
        }

        /// <summary>
        /// 报告进度
        /// </summary>
        private void ReportProgress(float progress, string message)
        {
            _progressCallback?.Invoke(progress, message);
        }

        #region 工具方法

        /// <summary>
        /// 生成总表变体集合（合并所有场景的变体）
        /// </summary>
        private void GenerateMasterVariantCollection()
        {
            if (_generatedVariantFiles.Count == 0)
            {
                Debug.LogWarning("[Master Collection] No variant files to merge!");
                _scenePaths = null;
                _currentState = CollectionState.None;
                ReportProgress(1f, "No variant files generated!");
                _completeCallback?.Invoke(false, "No variant files were generated during scene processing.");
                return;
            }

            ReportProgress(0.95f, "Generating master variant collection...");
            Debug.Log($"[Master Collection] Merging {_generatedVariantFiles.Count} variant files...");

            try
            {
                // 创建总表的清单
                var masterManifest = new ShaderVariantCollectionManifest();

                // 合并所有场景的变体
                foreach (var variantFilePath in _generatedVariantFiles)
                {
                    var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(variantFilePath);
                    if (svc == null)
                    {
                        Debug.LogWarning($"[Master Collection] Failed to load variant file: {variantFilePath}");
                        continue;
                    }

                    var manifest = ShaderVariantCollectionManifest.Extract(svc);
                    if (manifest != null)
                    {
                        // 合并变体信息
                        foreach (var shaderInfo in manifest.ShaderVariantInfos)
                        {
                            foreach (var variantElement in shaderInfo.ShaderVariantElements)
                            {
                                masterManifest.AddShaderVariant(
                                    shaderInfo.AssetPath,
                                    shaderInfo.ShaderName,
                                    variantElement.PassType,
                                    variantElement.Keywords
                                );
                            }
                        }
                    }

                    Debug.Log($"[Master Collection] Merged variants from: {Path.GetFileName(variantFilePath)}");
                }

                // 统计总数
                masterManifest.ShaderTotalCount = masterManifest.ShaderVariantInfos.Count;
                masterManifest.VariantTotalCount = 0;
                foreach (var info in masterManifest.ShaderVariantInfos)
                {
                    masterManifest.VariantTotalCount += info.ShaderVariantElements.Count;
                }

                Debug.Log($"[Master Collection] Total unique shaders: {masterManifest.ShaderTotalCount}");
                Debug.Log($"[Master Collection] Total unique variants: {masterManifest.VariantTotalCount}");

                // 保存总表文件
                string masterFilePath = NormalizePath(Path.Combine(_outputFolder, "_Master.shadervariants"));
                Debug.Log($"[Master Collection] Saving master file to: {masterFilePath}");

                // 删除旧文件
                AssetDatabase.DeleteAsset(masterFilePath);

                // 使用manifest重新创建ShaderVariantCollection
                CreateShaderVariantCollectionFromManifest(masterManifest, masterFilePath);

                // 生成JSON清单（如果启用）
                if (_exportJson)
                {
                    string jsonData = JsonUtility.ToJson(masterManifest, true);
                    string jsonPath = NormalizePath(Path.ChangeExtension(masterFilePath, ".json"));
                    WriteTextFileUnity(jsonPath, jsonData);
                    Debug.Log($"[Master Collection] JSON manifest saved to: {jsonPath}");
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                // 完成：先恢复原始场景，再通知UI
                _scenePaths = null;
                _generatedVariantFiles.Clear();
                _currentState = CollectionState.None;

                RestoreOriginalScene();

                ReportProgress(1f, "Master variant collection generated successfully!");
                _completeCallback?.Invoke(true,
                    $"Successfully processed {_totalSceneCount} scenes!\n" +
                    $"Generated {_totalSceneCount} scene variant files + 1 master file.\n" +
                    $"Master collection: Shaders: {masterManifest.ShaderTotalCount}, Variants: {masterManifest.VariantTotalCount}\n" +
                    $"Output folder: {_outputFolder}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Master Collection] Failed to generate master collection: {e.Message}\n{e.StackTrace}");
                _scenePaths = null;
                _generatedVariantFiles.Clear();
                _currentState = CollectionState.None;
                _completeCallback?.Invoke(false, $"Failed to generate master collection: {e.Message}");
            }
        }

        /// <summary>
        /// 从清单创建ShaderVariantCollection文件
        /// </summary>
        private void CreateShaderVariantCollectionFromManifest(ShaderVariantCollectionManifest manifest, string savePath)
        {
            try
            {
                // 创建新的ShaderVariantCollection资源
                var newSVC = new ShaderVariantCollection();
                int successfulVariants = 0;
                int failedVariants = 0;

                // 使用SerializedObject添加变体
                using (var so = new SerializedObject(newSVC))
                {
                    var shaderArray = so.FindProperty("m_Shaders");

                    foreach (var shaderInfo in manifest.ShaderVariantInfos)
                    {
                        // 加载Shader
                        Shader shader = null;
                        try
                        {
                            shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderInfo.AssetPath);
                            if (shader == null)
                            {
                                Debug.LogWarning($"[Create SVC] Failed to load shader at: {shaderInfo.AssetPath}");
                                failedVariants += shaderInfo.ShaderVariantElements.Count;
                                continue;
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[Create SVC] Exception loading shader at {shaderInfo.AssetPath}: {e.Message}");
                            failedVariants += shaderInfo.ShaderVariantElements.Count;
                            continue;
                        }

                        // 添加每个变体
                        foreach (var element in shaderInfo.ShaderVariantElements)
                        {
                            try
                            {
                                var variant = new ShaderVariantCollection.ShaderVariant(
                                    shader,
                                    element.PassType,
                                    element.Keywords
                                );

                                if (!newSVC.Contains(variant))
                                {
                                    newSVC.Add(variant);
                                    successfulVariants++;
                                }
                            }
                            catch (Exception e)
                            {
                                Debug.LogWarning($"[Create SVC] Failed to add variant for shader {shaderInfo.ShaderName}: {e.Message}");
                                failedVariants++;
                            }
                        }
                    }

                    so.ApplyModifiedProperties();
                }

                // 保存资源
                AssetDatabase.CreateAsset(newSVC, savePath);
                AssetDatabase.SaveAssets();

                Debug.Log($"[Create SVC] Saved ShaderVariantCollection to: {savePath}");
                Debug.Log($"[Create SVC] Successfully added {successfulVariants} variants, failed {failedVariants} variants");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Create SVC] Critical error creating ShaderVariantCollection: {e.Message}\n{e.StackTrace}");
                throw; // 重新抛出异常
            }
        }

        /// <summary>
        /// 计算批次间隔时间（根据材质数量动态调整）
        /// </summary>
        private float CalculateBatchSleepTime(int materialCount)
        {
            // 基础公式: 基础时间 + 每个材质增加一定时间
            float sleepTime = MinBatchSleepMilliseconds + (materialCount * SleepPerMaterial);

            // 限制在最小和最大值之间
            sleepTime = Mathf.Clamp(sleepTime, MinBatchSleepMilliseconds, MaxBatchSleepMilliseconds);

            Debug.Log($"[Batch Sleep] Calculated sleep time for {materialCount} materials: {sleepTime}ms");
            return sleepTime;
        }

        /// <summary>
        /// 深度扫描场景中的所有材质
        /// </summary>
        public static List<string> ScanMaterialsInScene(string scenePath)
        {
            HashSet<string> materialPaths = new HashSet<string>();

            // 保存当前场景状态
            var currentScene = EditorSceneManager.GetActiveScene();
            string currentScenePath = currentScene.path;
            bool shouldRestore = currentScenePath != scenePath;

            // 检查当前场景是否有未保存的修改（跳过临时场景，其路径为空字符串）
            if (shouldRestore && currentScene.isDirty && !string.IsNullOrEmpty(currentScenePath))
            {
                Debug.LogWarning($"Current scene '{currentScene.name}' has unsaved changes.");

                // 询问用户是否保存
                int choice = EditorUtility.DisplayDialogComplex(
                    "Unsaved Changes",
                    $"The current scene '{currentScene.name}' has unsaved changes.\n\nWould you like to save before scanning other scenes?",
                    "Save and Continue",
                    "Cancel Scan",
                    "Don't Save and Continue"
                );

                if (choice == 1) // Cancel
                {
                    Debug.LogWarning("[Scene Scanner] Scan cancelled by user due to unsaved changes.");
                    return new List<string>(materialPaths);
                }
                else if (choice == 0) // Save
                {
                    try
                    {
                        if (!EditorSceneManager.SaveScene(currentScene))
                        {
                            Debug.LogError($"Failed to save current scene '{currentScene.name}'.");
                            EditorUtility.DisplayDialog("Save Failed",
                                $"Failed to save scene '{currentScene.name}'. Scan cancelled.", "OK");
                            return new List<string>(materialPaths);
                        }
                        Debug.Log($"[Scene Scanner] Saved scene '{currentScene.name}' successfully.");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Exception while saving current scene '{currentScene.name}': {e.Message}");
                        EditorUtility.DisplayDialog("Save Error",
                            $"Error saving scene: {e.Message}\nScan cancelled.", "OK");
                        return new List<string>(materialPaths);
                    }
                }
                // choice == 2 (Don't Save) - 继续扫描
            }

            try
            {
                Scene scene;
                if (shouldRestore)
                {
                    // 目标场景与当前不同，先加载目标场景
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                }
                else
                {
                    // 目标场景已是当前活动场景，直接扫描，避免不必要的 reload
                    scene = EditorSceneManager.GetActiveScene();
                }

                // 获取场景中所有根GameObject
                var rootObjects = scene.GetRootGameObjects();

                foreach (var root in rootObjects)
                {
                    // 递归扫描所有GameObject
                    ScanGameObjectMaterials(root, materialPaths);
                }

                Debug.Log($"[Scene Scanner] Found {materialPaths.Count} unique materials in scene: {Path.GetFileNameWithoutExtension(scenePath)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to scan scene '{scenePath}': {e.Message}");
            }
            finally
            {
                // 恢复原场景
                if (shouldRestore && !string.IsNullOrEmpty(currentScenePath))
                {
                    try
                    {
                        EditorSceneManager.OpenScene(currentScenePath, OpenSceneMode.Single);
                        Debug.Log($"[Scene Scanner] Restored original scene: {Path.GetFileNameWithoutExtension(currentScenePath)}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"CRITICAL: Failed to restore original scene '{currentScenePath}': {e.Message}");
                        EditorUtility.DisplayDialog("Critical Error",
                            $"Failed to restore your original scene!\n\nScene path: {currentScenePath}\n\nPlease manually reopen your scene.",
                            "OK");
                    }
                }

                // 卸载未使用的资源
                Resources.UnloadUnusedAssets();
            }

            return new List<string>(materialPaths);
        }

        /// <summary>
        /// 获取BuildSettings中的所有场景路径
        /// </summary>
        public static List<string> GetScenesInBuildSettings()
        {
            List<string> scenePaths = new List<string>();

            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    scenePaths.Add(scene.path);
                }
            }

            return scenePaths;
        }

        /// <summary>
        /// 递归扫描GameObject及其子对象的所有材质
        /// </summary>
        private static void ScanGameObjectMaterials(GameObject obj, HashSet<string> materialPaths)
        {
            if (obj == null) return;

            // 扫描当前对象
            CollectMaterialsFromGameObject(obj, materialPaths, false);

            // 递归扫描子对象
            foreach (Transform child in obj.transform)
            {
                ScanGameObjectMaterials(child.gameObject, materialPaths);
            }
        }

        /// <summary>
        /// 从GameObject收集材质（通用方法）
        /// </summary>
        /// <param name="obj">要扫描的GameObject</param>
        /// <param name="materialPaths">材质路径集合</param>
        /// <param name="includeChildren">是否包含子对象（用于预制体扫描）</param>
        public static void CollectMaterialsFromGameObject(GameObject obj, HashSet<string> materialPaths, bool includeChildren)
        {
            if (obj == null) return;

            // 扫描 Renderer 组件 (MeshRenderer, SkinnedMeshRenderer, SpriteRenderer 等)
            var renderers = includeChildren ? obj.GetComponentsInChildren<Renderer>(true) : obj.GetComponents<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer != null && renderer.sharedMaterials != null)
                {
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        AddMaterialPath(mat, materialPaths);
                    }
                }
            }

            // 扫描 ParticleSystem
            if (includeChildren)
            {
                var particleSystems = obj.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in particleSystems)
                {
                    var particleRenderer = ps.GetComponent<ParticleSystemRenderer>();
                    if (particleRenderer != null && particleRenderer.sharedMaterials != null)
                    {
                        foreach (var mat in particleRenderer.sharedMaterials)
                        {
                            AddMaterialPath(mat, materialPaths);
                        }
                    }
                }
            }
            else
            {
                var particleSystem = obj.GetComponent<ParticleSystem>();
                if (particleSystem != null)
                {
                    var particleRenderer = obj.GetComponent<ParticleSystemRenderer>();
                    if (particleRenderer != null && particleRenderer.sharedMaterials != null)
                    {
                        foreach (var mat in particleRenderer.sharedMaterials)
                        {
                            AddMaterialPath(mat, materialPaths);
                        }
                    }
                }
            }

            // 扫描 UI Image 组件
            var images = includeChildren ? obj.GetComponentsInChildren<Image>(true) : obj.GetComponents<Image>();
            foreach (var image in images)
            {
                if (image != null && image.material != null)
                {
                    AddMaterialPath(image.material, materialPaths);
                }
            }

            // 扫描 UI RawImage 组件
            var rawImages = includeChildren ? obj.GetComponentsInChildren<RawImage>(true) : obj.GetComponents<RawImage>();
            foreach (var rawImage in rawImages)
            {
                if (rawImage != null && rawImage.material != null)
                {
                    AddMaterialPath(rawImage.material, materialPaths);
                }
            }

            // 扫描 UI Text 组件
            var texts = includeChildren ? obj.GetComponentsInChildren<Text>(true) : obj.GetComponents<Text>();
            foreach (var text in texts)
            {
                if (text != null && text.material != null)
                {
                    AddMaterialPath(text.material, materialPaths);
                }
            }
        }

        /// <summary>
        /// 添加材质路径到集合
        /// </summary>
        public static void AddMaterialPath(Material material, HashSet<string> materialPaths)
        {
            if (material == null) return;

            // 排除内置材质
            string path = AssetDatabase.GetAssetPath(material);
            if (!string.IsNullOrEmpty(path) &&
                !path.StartsWith("Library/") &&
                !path.StartsWith("Resources/unity_builtin_extra"))
            {
                materialPaths.Add(path);
            }
        }

        /// <summary>
        /// 路径规范化
        /// </summary>
        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        /// <summary>
        /// 确保目录存在（递归创建，使用Unity AssetDatabase API）
        /// </summary>
        private static void EnsureDirectoryExists(string directory)
        {
            directory = NormalizePath(directory);

            if (string.IsNullOrEmpty(directory) || directory == "Assets")
                return;

            // 检查目录是否已存在
            if (AssetDatabase.IsValidFolder(directory))
                return;

            // 先确保父目录存在
            string parentDir = NormalizePath(Path.GetDirectoryName(directory));
            if (!string.IsNullOrEmpty(parentDir))
            {
                EnsureDirectoryExists(parentDir);
            }

            // 创建当前目录
            string folderName = Path.GetFileName(directory);

            // 确保父目录有效
            if (string.IsNullOrEmpty(parentDir))
            {
                Debug.LogError($"Cannot create folder '{folderName}': invalid parent directory");
                return;
            }

            string guid = AssetDatabase.CreateFolder(parentDir, folderName);

            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"Failed to create folder: {directory}, falling back to System.IO");
                // 如果AssetDatabase创建失败，作为后备使用System.IO
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            else
            {
                Debug.Log($"Created folder: {directory}");
            }
        }

        /// <summary>
        /// 确保文件所在目录存在（使用Unity AssetDatabase API）
        /// </summary>
        private static void EnsureDirectoryExistsUnity(string filePath)
        {
            string directory = NormalizePath(Path.GetDirectoryName(filePath));
            EnsureDirectoryExists(directory);
        }

        /// <summary>
        /// 使用Unity规范方式写入文本文件
        /// </summary>
        private static void WriteTextFileUnity(string path, string content)
        {
            // 确保目录存在
            EnsureDirectoryExistsUnity(path);

            // 先写入文件系统
            try
            {
                File.WriteAllText(path, content);

                // 刷新AssetDatabase使Unity识别新文件
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

                Debug.Log($"Successfully wrote file: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to write file {path}: {e.Message}");
            }
        }

        /// <summary>
        /// 聚焦游戏窗口
        /// </summary>
        private static void FocusGameWindow()
        {
            try
            {
                var assembly = typeof(EditorWindow).Assembly;
                var gameViewType = assembly.GetType("UnityEditor.GameView");
                if (gameViewType != null)
                {
                    EditorWindow.GetWindow(gameViewType, false, "Game", true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to focus game window: {e.Message}");
            }
        }

        #endregion
    }
}
