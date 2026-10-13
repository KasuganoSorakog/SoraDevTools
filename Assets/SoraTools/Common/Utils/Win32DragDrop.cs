using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SoraTools.Utils
{
    public static class Win32DragDrop
    {
        private static readonly List<string> _droppedFiles = new List<string>();
        private static Vector2 _dropPoint;
        private static IntPtr _oldWndProc;
        private static IntPtr _hwnd;
        private static WndProcDelegate _wndProcDelegate;
        private static bool _enabled;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_DROPFILES = 0x0233;

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(IntPtr hwnd, bool fAccept);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, IntPtr lpszFile, uint cch);

        [DllImport("shell32.dll")]
        private static extern bool DragQueryPoint(IntPtr hDrop, out POINT lppt);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(IntPtr hDrop);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int GWLP_WNDPROC = -4;

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            return SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        public static void Enable()
        {
            if (_enabled) return;

            _hwnd = GetActiveWindow();
            if (_hwnd == IntPtr.Zero) return;

            DragAcceptFiles(_hwnd, true);

            _wndProcDelegate = WndProc;
            IntPtr newWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
            _oldWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, newWndProc);
            _enabled = true;
        }

        public static void Disable()
        {
            if (!_enabled) return;
            if (_hwnd != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _oldWndProc);
                DragAcceptFiles(_hwnd, false);
            }
            _enabled = false;
            _oldWndProc = IntPtr.Zero;
            _hwnd = IntPtr.Zero;
        }

        public static string[] GetDroppedFiles()
        {
            lock (_droppedFiles)
            {
                if (_droppedFiles.Count == 0) return null;
                var result = _droppedFiles.ToArray();
                _droppedFiles.Clear();
                return result;
            }
        }

        public static Vector2 GetDropPoint()
        {
            lock (_droppedFiles) { return _dropPoint; }
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_DROPFILES)
            {
                IntPtr hDrop = wParam;
                uint count = DragQueryFile(hDrop, 0xFFFFFFFF, IntPtr.Zero, 0);

                lock (_droppedFiles)
                {
                    DragQueryPoint(hDrop, out POINT pt);
                    _dropPoint = new Vector2(pt.x, pt.y);
                    for (uint i = 0; i < count; i++)
                    {
                        uint size = DragQueryFile(hDrop, i, IntPtr.Zero, 0) + 1;
                        IntPtr buffer = Marshal.AllocHGlobal((int)(size * 2));
                        try
                        {
                            DragQueryFile(hDrop, i, buffer, size);
                            string path = Marshal.PtrToStringUni(buffer);
                            if (!string.IsNullOrEmpty(path))
                                _droppedFiles.Add(path);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(buffer);
                        }
                    }
                }

                DragFinish(hDrop);
                return IntPtr.Zero;
            }

            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }
    }
}
