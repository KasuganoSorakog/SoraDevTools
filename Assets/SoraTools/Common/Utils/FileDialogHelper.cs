using System;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

namespace SoraTools.Utils
{
    /// <summary>
    /// Cross-platform file dialog utility.
    /// Uses EditorUtility in Editor mode and Win32 API in standalone builds.
    /// </summary>
    public static class FileDialogHelper
    {
        private static string _lastBrowsePath = "";

#if UNITY_STANDALONE_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public string lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileName([In, Out] ref OpenFileName ofn);
#endif

        public static string OpenApkFileDialog()
        {
#if UNITY_EDITOR
            string path = EditorUtility.OpenFilePanel("选择APK文件", GetInitialDir(), "apk");
            if (!string.IsNullOrEmpty(path)) _lastBrowsePath = path;
            return path ?? "";
#elif UNITY_STANDALONE_WIN
            return OpenWin32Dialog("选择APK文件", "APK Files (*.apk)\0*.apk\0All Files (*.*)\0*.*\0");
#else
            Debug.LogWarning("[FileDialogHelper] File dialog not supported on this platform");
            return "";
#endif
        }

        public static string OpenObbFileDialog()
        {
#if UNITY_EDITOR
            string path = EditorUtility.OpenFilePanel("选择OBB文件", GetInitialDir(), "obb");
            if (!string.IsNullOrEmpty(path)) _lastBrowsePath = path;
            return path ?? "";
#elif UNITY_STANDALONE_WIN
            return OpenWin32Dialog("选择OBB文件", "OBB Files (*.obb)\0*.obb\0All Files (*.*)\0*.*\0");
#else
            Debug.LogWarning("[FileDialogHelper] File dialog not supported on this platform");
            return "";
#endif
        }

        public static string OpenLogFileDialog()
        {
#if UNITY_EDITOR
            string path = EditorUtility.OpenFilePanel("选择日志文件", GetInitialDir(), "txt,log");
            if (!string.IsNullOrEmpty(path)) _lastBrowsePath = path;
            return path ?? "";
#elif UNITY_STANDALONE_WIN
            return OpenWin32Dialog("选择日志文件", "Log Files (*.txt;*.log)\0*.txt;*.log\0All Files (*.*)\0*.*\0");
#else
            Debug.LogWarning("[FileDialogHelper] File dialog not supported on this platform");
            return "";
#endif
        }

#if UNITY_STANDALONE_WIN
        private static string OpenWin32Dialog(string title, string filter)
        {
            try
            {
                var ofn = new OpenFileName();
                ofn.lStructSize    = Marshal.SizeOf(ofn);
                ofn.lpstrFilter    = filter;
                ofn.nFilterIndex   = 1;
                ofn.lpstrFile      = new string(new char[512]);
                ofn.nMaxFile       = 512;
                ofn.lpstrFileTitle = new string(new char[128]);
                ofn.nMaxFileTitle  = 128;
                ofn.lpstrInitialDir = GetInitialDir();
                ofn.lpstrTitle     = title;
                // OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_NONETWORKBUTTON
                ofn.Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008 | 0x00020000;

                if (GetOpenFileName(ref ofn))
                {
                    _lastBrowsePath = ofn.lpstrFile;
                    return ofn.lpstrFile;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileDialogHelper] Dialog error: {ex.Message}");
            }
            return "";
        }
#endif

        /// <summary>Open the folder containing the specified file in Explorer.</summary>
        public static void OpenInExplorer(string filePath)
        {
            try
            {
                string dir = File.Exists(filePath)
                    ? Path.GetDirectoryName(filePath)
                    : filePath;

                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                    if (File.Exists(filePath))
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                    else
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                    System.Diagnostics.Process.Start("open", $"-R \"{filePath}\"");
#else
                    System.Diagnostics.Process.Start("xdg-open", $"\"{dir}\"");
#endif
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileDialogHelper] Cannot open explorer: {ex.Message}");
            }
        }

        private static string GetInitialDir()
        {
            if (!string.IsNullOrEmpty(_lastBrowsePath))
            {
                string dir = Path.GetDirectoryName(_lastBrowsePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    return dir;
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }
    }
}
