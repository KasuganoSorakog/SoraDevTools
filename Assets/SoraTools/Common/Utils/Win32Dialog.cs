using System;
using System.Runtime.InteropServices;

namespace SoraTools.Utils
{
    public static class Win32Dialog
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENFILENAME
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
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetOpenFileName(ref OPENFILENAME lpofn);

        private const int OFN_ALLOWMULTISELECT = 0x00000200;
        private const int OFN_EXPLORER         = 0x00080000;
        private const int OFN_FILEMUSTEXIST    = 0x00001000;
        private const int OFN_PATHMUSTEXIST    = 0x00000800;
        private const int OFN_NOCHANGEDIR      = 0x00000008;

        public static string[] OpenFileDialog(string title = "Select Files", string filter = "All Files\0*.*\0", bool multiSelect = true)
        {
            var ofn = new OPENFILENAME();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner   = IntPtr.Zero;
            ofn.lpstrFilter = filter;
            ofn.lpstrFile   = new string('\0', 65536);
            ofn.nMaxFile    = 65536;
            ofn.lpstrTitle  = title;
            ofn.Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
            if (multiSelect) ofn.Flags |= OFN_ALLOWMULTISELECT;

            if (GetOpenFileName(ref ofn))
            {
                string result = ofn.lpstrFile;
                string[] parts = result.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return null;
                if (parts.Length == 1) return parts;

                // Multi-select: first element is directory, rest are filenames
                string dir = parts[0];
                string[] files = new string[parts.Length - 1];
                for (int i = 1; i < parts.Length; i++)
                    files[i - 1] = System.IO.Path.Combine(dir, parts[i]);
                return files;
            }
            return null;
        }

        // ── Folder browser using SHBrowseForFolder ──

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

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SHGetPathFromIDList(IntPtr pidl, IntPtr pszPath);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr ptr);

        private const uint BIF_RETURNONLYFSDIRS = 0x00000001;
        private const uint BIF_NEWDIALOGSTYLE   = 0x00000040;

        public static string OpenFolderDialog(string title = "Select Folder")
        {
            var bi = new BROWSEINFO();
            bi.hwndOwner       = IntPtr.Zero;
            bi.pszDisplayName  = new string('\0', 260);
            bi.lpszTitle       = title;
            bi.ulFlags         = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;

            IntPtr pidl = SHBrowseForFolder(ref bi);
            if (pidl != IntPtr.Zero)
            {
                IntPtr pathPtr = Marshal.AllocHGlobal(520);
                try
                {
                    if (SHGetPathFromIDList(pidl, pathPtr))
                        return Marshal.PtrToStringUni(pathPtr);
                }
                finally
                {
                    Marshal.FreeHGlobal(pathPtr);
                    CoTaskMemFree(pidl);
                }
            }
            return null;
        }
    }
}
