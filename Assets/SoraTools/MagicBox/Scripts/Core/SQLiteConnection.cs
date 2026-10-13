using System;
using System.Collections.Generic;
using UnityEngine;

namespace MagicBox.Core
{
    public class SQLiteConnection : IDisposable
    {
        private IntPtr _db;
        private bool _disposed;

        public bool IsOpen => _db != IntPtr.Zero;

        public SQLiteConnection(string databasePath)
        {
            int rc = SQLiteRaw.Open(databasePath, out _db,
                SQLiteRaw.OPEN_READWRITE | SQLiteRaw.OPEN_CREATE);
            if (rc != SQLiteRaw.OK)
            {
                string err = _db != IntPtr.Zero ? SQLiteRaw.ErrorMessage(_db) : "Unknown error";
                throw new Exception($"Failed to open database '{databasePath}': {err}");
            }
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA busy_timeout=3000;");
            Execute("PRAGMA foreign_keys=ON;");
        }

        public void Execute(string sql, params object[] args)
        {
            IntPtr stmt;
            int rc = SQLiteRaw.Prepare(_db, sql, out stmt);
            if (rc != SQLiteRaw.OK)
                throw new Exception($"SQL prepare error: {SQLiteRaw.ErrorMessage(_db)}\nSQL: {sql}");
            try
            {
                BindParameters(stmt, args);
                rc = SQLiteRaw.Step(stmt);
                if (rc != SQLiteRaw.DONE && rc != SQLiteRaw.ROW)
                    throw new Exception($"SQL execute error: {SQLiteRaw.ErrorMessage(_db)}\nSQL: {sql}");
            }
            finally { SQLiteRaw.Finalize(stmt); }
        }

        public List<Dictionary<string, object>> Query(string sql, params object[] args)
        {
            var results = new List<Dictionary<string, object>>();
            IntPtr stmt;
            int rc = SQLiteRaw.Prepare(_db, sql, out stmt);
            if (rc != SQLiteRaw.OK)
                throw new Exception($"SQL prepare error: {SQLiteRaw.ErrorMessage(_db)}\nSQL: {sql}");
            try
            {
                BindParameters(stmt, args);
                int colCount = SQLiteRaw.ColumnCount(stmt);
                while (SQLiteRaw.Step(stmt) == SQLiteRaw.ROW)
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < colCount; i++)
                    {
                        string name = SQLiteRaw.ColumnName(stmt, i);
                        int type = SQLiteRaw.ColumnType(stmt, i);
                        switch (type)
                        {
                            case SQLiteRaw.TYPE_INTEGER: row[name] = SQLiteRaw.ColumnInt64(stmt, i); break;
                            case SQLiteRaw.TYPE_FLOAT: row[name] = SQLiteRaw.ColumnDouble(stmt, i); break;
                            case SQLiteRaw.TYPE_TEXT: row[name] = SQLiteRaw.ColumnText(stmt, i); break;
                            case SQLiteRaw.TYPE_NULL: row[name] = null; break;
                            default: row[name] = SQLiteRaw.ColumnText(stmt, i); break;
                        }
                    }
                    results.Add(row);
                }
            }
            finally { SQLiteRaw.Finalize(stmt); }
            return results;
        }

        public T Scalar<T>(string sql, params object[] args)
        {
            IntPtr stmt;
            int rc = SQLiteRaw.Prepare(_db, sql, out stmt);
            if (rc != SQLiteRaw.OK)
                throw new Exception($"SQL prepare error: {SQLiteRaw.ErrorMessage(_db)}\nSQL: {sql}");
            try
            {
                BindParameters(stmt, args);
                if (SQLiteRaw.Step(stmt) == SQLiteRaw.ROW)
                {
                    int type = SQLiteRaw.ColumnType(stmt, 0);
                    object val;
                    switch (type)
                    {
                        case SQLiteRaw.TYPE_INTEGER: val = SQLiteRaw.ColumnInt64(stmt, 0); break;
                        case SQLiteRaw.TYPE_FLOAT: val = SQLiteRaw.ColumnDouble(stmt, 0); break;
                        case SQLiteRaw.TYPE_TEXT: val = SQLiteRaw.ColumnText(stmt, 0); break;
                        default: val = null; break;
                    }
                    if (val == null) return default;
                    return (T)Convert.ChangeType(val, typeof(T));
                }
                return default;
            }
            finally { SQLiteRaw.Finalize(stmt); }
        }

        private void BindParameters(IntPtr stmt, object[] args)
        {
            if (args == null) return;
            for (int i = 0; i < args.Length; i++)
            {
                int idx = i + 1;
                object val = args[i];
                if (val == null) SQLiteRaw.BindNull(stmt, idx);
                else if (val is string s) SQLiteRaw.BindText(stmt, idx, s);
                else if (val is int intVal) SQLiteRaw.BindInt(stmt, idx, intVal);
                else if (val is long longVal) SQLiteRaw.BindInt64(stmt, idx, longVal);
                else if (val is double dVal) SQLiteRaw.BindDouble(stmt, idx, dVal);
                else if (val is float fVal) SQLiteRaw.BindDouble(stmt, idx, fVal);
                else if (val is bool bVal) SQLiteRaw.BindInt(stmt, idx, bVal ? 1 : 0);
                else if (val is Enum) SQLiteRaw.BindInt(stmt, idx, Convert.ToInt32(val));
                else SQLiteRaw.BindText(stmt, idx, val.ToString());
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_db != IntPtr.Zero)
            {
                try { Execute("PRAGMA wal_checkpoint(TRUNCATE);"); } catch { }
                SQLiteRaw.Close(_db);
                _db = IntPtr.Zero;
            }
        }
    }
}
