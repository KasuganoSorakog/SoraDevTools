using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace ShaderVariantCollectorTool
{
    /// <summary>
    /// Shader变体集合清单
    /// 用于序列化和导出Shader变体信息
    /// </summary>
    [Serializable]
    public class ShaderVariantCollectionManifest
    {
        /// <summary>
        /// Shader变体元素
        /// </summary>
        [Serializable]
        public class ShaderVariantElement
        {
            /// <summary>
            /// Pass类型
            /// </summary>
            public PassType PassType;

            /// <summary>
            /// 关键字数组
            /// </summary>
            public string[] Keywords;
        }

        /// <summary>
        /// Shader变体信息
        /// </summary>
        [Serializable]
        public class ShaderVariantInfo
        {
            /// <summary>
            /// Shader资源路径
            /// </summary>
            public string AssetPath;

            /// <summary>
            /// Shader名称
            /// </summary>
            public string ShaderName;

            /// <summary>
            /// Shader变体元素列表
            /// </summary>
            public List<ShaderVariantElement> ShaderVariantElements = new List<ShaderVariantElement>(1000);
        }

        /// <summary>
        /// Shader总数量
        /// </summary>
        public int ShaderTotalCount;

        /// <summary>
        /// 变体总数量
        /// </summary>
        public int VariantTotalCount;

        /// <summary>
        /// Shader变体信息列表
        /// </summary>
        public List<ShaderVariantInfo> ShaderVariantInfos = new List<ShaderVariantInfo>(1000);

        // 用于快速查找的缓存（不序列化）
        [NonSerialized]
        private Dictionary<string, ShaderVariantInfo> _shaderCache;

        /// <summary>
        /// 添加Shader变体信息（自动去重）
        /// </summary>
        public void AddShaderVariant(string assetPath, string shaderName, PassType passType, string[] keywords)
        {
            var info = GetOrCreateShaderVariantInfo(assetPath, shaderName);

            // 检查是否已存在相同的变体（去重）
            foreach (var existingElement in info.ShaderVariantElements)
            {
                if (existingElement.PassType == passType && AreKeywordsEqual(existingElement.Keywords, keywords))
                {
                    // 已存在相同的变体，跳过
                    return;
                }
            }

            // 添加新变体
            ShaderVariantElement element = new ShaderVariantElement();
            element.PassType = passType;
            element.Keywords = keywords;
            info.ShaderVariantElements.Add(element);
        }

        /// <summary>
        /// 比较两个关键字数组是否完全相等（含重复元素的严格比较）
        /// </summary>
        private bool AreKeywordsEqual(string[] keywords1, string[] keywords2)
        {
            if (keywords1 == null && keywords2 == null)
                return true;

            if (keywords1 == null || keywords2 == null)
                return false;

            if (keywords1.Length != keywords2.Length)
                return false;

            if (keywords1.Length == 0)
                return true;

            // 排序后逐一比较，正确处理含重复元素的数组
            // Shader关键字数量通常 <= 16，排序开销可接受
            string[] sorted1 = (string[])keywords1.Clone();
            string[] sorted2 = (string[])keywords2.Clone();
            System.Array.Sort(sorted1, StringComparer.Ordinal);
            System.Array.Sort(sorted2, StringComparer.Ordinal);

            for (int i = 0; i < sorted1.Length; i++)
            {
                if (sorted1[i] != sorted2[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 获取或创建Shader变体信息
        /// </summary>
        private ShaderVariantInfo GetOrCreateShaderVariantInfo(string assetPath, string shaderName)
        {
            // 初始化缓存
            if (_shaderCache == null)
            {
                _shaderCache = new Dictionary<string, ShaderVariantInfo>();
                foreach (var info in ShaderVariantInfos)
                {
                    string key = GetCacheKey(info.AssetPath, info.ShaderName);
                    _shaderCache[key] = info;
                }
            }

            // 从缓存中查找
            string cacheKey = GetCacheKey(assetPath, shaderName);
            if (_shaderCache.TryGetValue(cacheKey, out ShaderVariantInfo cachedInfo))
            {
                return cachedInfo;
            }

            // 创建新的
            ShaderVariantInfo newInfo = new ShaderVariantInfo();
            newInfo.AssetPath = assetPath;
            newInfo.ShaderName = shaderName;
            ShaderVariantInfos.Add(newInfo);
            _shaderCache[cacheKey] = newInfo;
            return newInfo;
        }

        /// <summary>
        /// 生成缓存键
        /// </summary>
        private static string GetCacheKey(string assetPath, string shaderName)
        {
            return $"{assetPath}|{shaderName}";
        }

        /// <summary>
        /// 从ShaderVariantCollection文件中提取数据并生成清单
        /// </summary>
        public static ShaderVariantCollectionManifest Extract(ShaderVariantCollection svc)
        {
            if (svc == null)
            {
                Debug.LogError("ShaderVariantCollection is null");
                return null;
            }

            var manifest = new ShaderVariantCollectionManifest();
            // 注意：ShaderTotalCount / VariantTotalCount 在遍历完 svc 后从 manifest 自身统计，
            // 不再依赖全局运行时集合（避免多场景模式下计数对应错误的场景数据）

            using (var so = new SerializedObject(svc))
            {
                var shaderArray = so.FindProperty("m_Shaders.Array");
                if (shaderArray != null && shaderArray.isArray)
                {
                    for (int i = 0; i < shaderArray.arraySize; ++i)
                    {
                        var shaderRef = shaderArray.FindPropertyRelative($"data[{i}].first");
                        var shaderVariantsArray = shaderArray.FindPropertyRelative($"data[{i}].second.variants");

                        if (shaderRef != null && shaderRef.propertyType == SerializedPropertyType.ObjectReference &&
                            shaderVariantsArray != null && shaderVariantsArray.isArray)
                        {
                            var shader = shaderRef.objectReferenceValue as Shader;
                            if (shader == null)
                            {
                                Debug.LogWarning($"Invalid shader at index {i} in ShaderVariantCollection file.");
                                continue;
                            }

                            string shaderAssetPath = AssetDatabase.GetAssetPath(shader);
                            string shaderName = shader.name;

                            // 添加变体信息
                            for (int j = 0; j < shaderVariantsArray.arraySize; ++j)
                            {
                                var propKeywords = shaderVariantsArray.FindPropertyRelative($"Array.data[{j}].keywords");
                                var propPassType = shaderVariantsArray.FindPropertyRelative($"Array.data[{j}].passType");

                                if (propKeywords != null && propPassType != null && propKeywords.propertyType == SerializedPropertyType.String)
                                {
                                    // 分割关键字并过滤空值
                                    string keywordString = propKeywords.stringValue;
                                    string[] keywords = string.IsNullOrEmpty(keywordString)
                                        ? new string[0]
                                        : keywordString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                                    PassType passType = (PassType)propPassType.intValue;
                                    manifest.AddShaderVariant(shaderAssetPath, shaderName, passType, keywords);
                                }
                            }
                        }
                    }
                }
            }

            // 从 manifest 本身统计正确数量（而非从全局运行时集合读取）
            manifest.ShaderTotalCount  = manifest.ShaderVariantInfos.Count;
            manifest.VariantTotalCount = 0;
            foreach (var info in manifest.ShaderVariantInfos)
                manifest.VariantTotalCount += info.ShaderVariantElements.Count;

            return manifest;
        }
    }
}
