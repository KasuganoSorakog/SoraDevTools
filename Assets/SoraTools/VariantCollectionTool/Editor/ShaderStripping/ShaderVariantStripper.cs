using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

// 该脚本用于打包时剥离 Shader 变体，降低 Build 体积。
// 关键字列表和启用状态通过配置文件管理：
//   Edit → Project Settings → Sora Tools → Shader Variant Stripper
namespace SoraTools
{
    public class ShaderVariantStripper : IPreprocessShaders
    {
        public int callbackOrder => 0;

        // Unity 每次 Build 创建新实例，缓存在实例上即可复用整个 Build 过程
        private ShaderVariantStripperSettings _settings;
        private HashSet<string> _stripSet;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            // 仅在首次调用时加载配置（整个 Build 过程中复用）
            if (_settings == null)
                _settings = ShaderVariantStripperSettings.GetOrCreate();

            // 未启用时直接跳过，不做任何剥离
            if (!_settings.isEnabled)
                return;

            if (_settings.keywordsToStrip == null || _settings.keywordsToStrip.Count == 0)
                return;

            // 仅在首次调用时构建查找集合（整个 Build 过程中复用）
            _stripSet ??= new HashSet<string>(_settings.keywordsToStrip);

            int strippedCount = 0;

            // 反向遍历，安全地移除元素
            for (int i = data.Count - 1; i >= 0; --i)
            {
                bool shouldStrip = false;

                foreach (var keyword in data[i].shaderKeywordSet.GetShaderKeywords())
                {
                    if (_stripSet.Contains(keyword.name))
                    {
                        shouldStrip = true;
                        break;
                    }
                }

                if (shouldStrip)
                {
                    data.RemoveAt(i);
                    strippedCount++;
                }
            }

            if (strippedCount > 0)
            {
                Debug.Log($"[ShaderVariantStripper] '{shader.name}': stripped {strippedCount} variant(s).");
            }
        }
    }
}
