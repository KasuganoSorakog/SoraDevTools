using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace YtDownloader
{
    /// <summary>
    /// Windows Shell folder-picker dialog via P/Invoke.
    /// Only call at runtime on Windows (PC builds).
    /// </summary>
    public static class FolderBrowserHelper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SHGetPathFromIDList(IntPtr pidl,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszPath);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr ptr);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BROWSEINFO
        {
            public IntPtr hwndOwner;
            public IntPtr pidlRoot;
            public string pszDisplayName;
            public string lpszTitle;
            public uint   ulFlags;
            public IntPtr lpfn;
            public IntPtr lParam;
            public int    iImage;
        }

        private const uint BIF_RETURNONLYFSDIRS  = 0x0001;
        private const uint BIF_NEWDIALOGSTYLE    = 0x0040;
        private const uint BIF_EDITBOX           = 0x0010;

        /// <summary>
        /// Opens a Windows folder browser dialog. Returns null if cancelled.
        /// Must be called on the main thread.
        /// </summary>
        public static string Browse(string title = "选择保存目录")
        {
            try
            {
                var bi = new BROWSEINFO
                {
                    lpszTitle = title,
                    ulFlags   = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | BIF_EDITBOX,
                    pszDisplayName = new string('\0', 260),
                };

                IntPtr pidl = SHBrowseForFolder(ref bi);
                if (pidl == IntPtr.Zero) return null;

                var sb = new StringBuilder(260);
                bool ok = SHGetPathFromIDList(pidl, sb);
                CoTaskMemFree(pidl);

                return ok && sb.Length > 0 ? sb.ToString() : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[FolderBrowserHelper] Browse failed: " + ex.Message);
                return null;
            }
        }
    }
}
