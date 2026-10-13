using System;
using System.IO;
using UnityEngine;

namespace SoraTools
{
    [Serializable]
    internal class CommandHistoryData
    {
        public string deviceIp = "";
        public string installApkPath = "";
        public string installObbPath = "";
        public string installAbPath = "";
        public string screencastIp = "";
        public string logcatFilter = "";
        public string perfPackageName = "";
        public string pairIp = "";
        public string fileManagerDevicePath = "";
        public string fileManagerLocalPath = "";
    }

    internal static class CommandHistory
    {
        private static string FilePath =>
            Path.Combine(Application.persistentDataPath, "adb_command_history.json");

        public static CommandHistoryData Load()
        {
            try
            {
                string path = FilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    if (!string.IsNullOrEmpty(json))
                        return JsonUtility.FromJson<CommandHistoryData>(json) ?? new CommandHistoryData();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CommandHistory] Load failed: {ex.Message}");
            }

            return new CommandHistoryData();
        }

        public static void Save(CommandHistoryData data)
        {
            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CommandHistory] Save failed: {ex.Message}");
            }
        }
    }
}
