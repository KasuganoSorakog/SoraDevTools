using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MagicBox.Core
{
    internal static class SQLiteRaw
    {
        private const string DLL = "sqlite3";

        internal const int OK = 0;
        internal const int ERROR = 1;
        internal const int BUSY = 5;
        internal const int ROW = 100;
        internal const int DONE = 101;

        internal const int OPEN_READWRITE = 0x00000002;
        internal const int OPEN_CREATE = 0x00000004;

        internal const int TYPE_INTEGER = 1;
        internal const int TYPE_FLOAT = 2;
        internal const int TYPE_TEXT = 3;
        internal const int TYPE_BLOB = 4;
        internal const int TYPE_NULL = 5;

        private static readonly IntPtr TRANSIENT = new IntPtr(-1);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_open_v2")]
        private static extern int _open(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

        internal static int Open(string path, out IntPtr db, int flags)
        {
            return _open(ToUTF8(path), out db, flags, IntPtr.Zero);
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_close")]
        internal static extern int Close(IntPtr db);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_prepare_v2")]
        private static extern int _prepare(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);

        internal static int Prepare(IntPtr db, string sql, out IntPtr stmt)
        {
            byte[] bytes = ToUTF8(sql);
            return _prepare(db, bytes, bytes.Length, out stmt, IntPtr.Zero);
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_step")]
        internal static extern int Step(IntPtr stmt);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_finalize")]
        internal static extern int Finalize(IntPtr stmt);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_reset")]
        internal static extern int Reset(IntPtr stmt);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_text")]
        private static extern int _bindText(IntPtr stmt, int idx, byte[] val, int n, IntPtr dest);

        internal static int BindText(IntPtr stmt, int idx, string val)
        {
            if (val == null) return BindNull(stmt, idx);
            byte[] bytes = Encoding.UTF8.GetBytes(val);
            return _bindText(stmt, idx, bytes, bytes.Length, TRANSIENT);
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_int")]
        internal static extern int BindInt(IntPtr stmt, int idx, int val);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_int64")]
        internal static extern int BindInt64(IntPtr stmt, int idx, long val);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_double")]
        internal static extern int BindDouble(IntPtr stmt, int idx, double val);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_null")]
        internal static extern int BindNull(IntPtr stmt, int idx);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_count")]
        internal static extern int ColumnCount(IntPtr stmt);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_type")]
        internal static extern int ColumnType(IntPtr stmt, int idx);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_text")]
        private static extern IntPtr _columnText(IntPtr stmt, int idx);

        internal static string ColumnText(IntPtr stmt, int idx)
        {
            return PtrToUTF8(_columnText(stmt, idx));
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_int")]
        internal static extern int ColumnInt(IntPtr stmt, int idx);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_int64")]
        internal static extern long ColumnInt64(IntPtr stmt, int idx);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_double")]
        internal static extern double ColumnDouble(IntPtr stmt, int idx);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_name")]
        private static extern IntPtr _columnName(IntPtr stmt, int idx);

        internal static string ColumnName(IntPtr stmt, int idx)
        {
            return PtrToUTF8(_columnName(stmt, idx));
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_errmsg")]
        private static extern IntPtr _errmsg(IntPtr db);

        internal static string ErrorMessage(IntPtr db)
        {
            return PtrToUTF8(_errmsg(db));
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_last_insert_rowid")]
        internal static extern long LastInsertRowId(IntPtr db);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_changes")]
        internal static extern int Changes(IntPtr db);

        private static byte[] ToUTF8(string s)
        {
            if (s == null) return new byte[] { 0 };
            byte[] strBytes = Encoding.UTF8.GetBytes(s);
            byte[] result = new byte[strBytes.Length + 1];
            Array.Copy(strBytes, result, strBytes.Length);
            return result;
        }

        private static string PtrToUTF8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            if (len == 0) return string.Empty;
            byte[] buffer = new byte[len];
            Marshal.Copy(ptr, buffer, 0, len);
            return Encoding.UTF8.GetString(buffer);
        }
    }
}
