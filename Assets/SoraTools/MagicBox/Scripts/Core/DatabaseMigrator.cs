using System;

namespace MagicBox.Core
{
    /// <summary>
    /// Versioned database schema migration system.
    /// Add new migrations as private methods and call them in Migrate().
    /// </summary>
    public class DatabaseMigrator
    {
        private readonly SQLiteConnection _conn;
        private const int LatestVersion = 1;

        public DatabaseMigrator(SQLiteConnection conn)
        {
            _conn = conn;
        }

        /// <summary>Current schema version of the database.</summary>
        public int CurrentVersion => GetVersion();

        /// <summary>Run all pending migrations up to LatestVersion.</summary>
        public void Migrate()
        {
            EnsureVersionTable();
            int ver = GetVersion();
            if (ver >= LatestVersion) return;

            if (ver < 1) RunMigration(1, MigrateV1);
            // Future: if (ver < 2) RunMigration(2, MigrateV2);
        }

        /// <summary>Execute a migration within a transaction for atomicity.</summary>
        private void RunMigration(int version, Action action)
        {
            _conn.Execute("BEGIN");
            try
            {
                action();
                SetVersion(version);
                _conn.Execute("COMMIT");
            }
            catch
            {
                try { _conn.Execute("ROLLBACK"); } catch { }
                throw;
            }
        }

        private void EnsureVersionTable()
        {
            _conn.Execute("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)");
            var rows = _conn.Query("SELECT version FROM schema_version");
            if (rows.Count == 0)
                _conn.Execute("INSERT INTO schema_version (version) VALUES (0)");
        }

        private int GetVersion()
        {
            try
            {
                return (int)_conn.Scalar<long>("SELECT version FROM schema_version LIMIT 1");
            }
            catch { return 0; }
        }

        private void SetVersion(int v)
        {
            _conn.Execute("UPDATE schema_version SET version=?", v);
        }

        // ============ Migrations ============

        /// <summary>
        /// V1: Add performance indexes for duplicate detection and name sorting.
        /// </summary>
        private void MigrateV1()
        {
            _conn.Execute("CREATE INDEX IF NOT EXISTS idx_res_hash ON resources(file_hash)");
            _conn.Execute("CREATE INDEX IF NOT EXISTS idx_res_name ON resources(name)");
        }

        // Template for future migrations:
        // private void MigrateV2()
        // {
        //     _conn.Execute("ALTER TABLE resources ADD COLUMN custom_field TEXT DEFAULT ''");
        // }
    }
}
