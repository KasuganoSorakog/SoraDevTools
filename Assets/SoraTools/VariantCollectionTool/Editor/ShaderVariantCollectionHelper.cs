using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace ShaderVariantCollectorTool
{
    /// <summary>
    /// Shader变体收集辅助类
    /// 封装对ShaderUtil非公开方法的反射调用
    /// </summary>
    public static class ShaderVariantCollectionHelper
    {
        /// <summary>
        /// 调用ShaderUtil的非公开静态方法
        /// </summary>
        private static object InvokeShaderUtilMethod(string methodName, params object[] parameters)
        {
            var method = typeof(ShaderUtil).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                Debug.LogError($"ShaderUtil.{methodName} not found. This may be a Unity version compatibility issue.");
                return null;
            }

            try
            {
                return method.Invoke(null, parameters);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to invoke ShaderUtil.{methodName}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 清空当前的Shader变体集合
        /// </summary>
        public static void ClearCurrentShaderVariantCollection()
        {
            InvokeShaderUtilMethod("ClearCurrentShaderVariantCollection");
        }

        /// <summary>
        /// 保存当前的Shader变体集合到指定路径
        /// </summary>
        public static void SaveCurrentShaderVariantCollection(string savePath)
        {
            InvokeShaderUtilMethod("SaveCurrentShaderVariantCollection", savePath);
        }

        /// <summary>
        /// 获取当前Shader变体集合中的Shader数量
        /// </summary>
        public static int GetCurrentShaderVariantCollectionShaderCount()
        {
            var result = InvokeShaderUtilMethod("GetCurrentShaderVariantCollectionShaderCount");
            return result != null ? (int)result : 0;
        }

        /// <summary>
        /// 获取当前Shader变体集合中的变体总数量
        /// </summary>
        public static int GetCurrentShaderVariantCollectionVariantCount()
        {
            var result = InvokeShaderUtilMethod("GetCurrentShaderVariantCollectionVariantCount");
            return result != null ? (int)result : 0;
        }

        /// <summary>
        /// 获取指定Shader的变体总数量
        /// </summary>
        public static int GetShaderVariantCount(Shader shader, bool usedBySceneOnly = true)
        {
            if (shader == null)
                return 0;

            var result = InvokeShaderUtilMethod("GetVariantCount", shader, usedBySceneOnly);
            return result != null ? (int)result : 0;
        }
    }
}
