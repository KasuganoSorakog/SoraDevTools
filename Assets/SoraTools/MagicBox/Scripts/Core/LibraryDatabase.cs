using System;
using System.Collections.Generic;
using System.Linq;
using MagicBox.Models;

namespace MagicBox.Core
{
    public class LibraryDatabase : IDisposable
    {
        private SQLiteConnection _conn;
        private bool _fts5Available;
        public bool IsOpen => _conn != null && _conn.IsOpen;
        public bool IsFTS5Available => _fts5Available;

        private readonly object _lock = new object();

        public void Open(string dbPath)
        {
            Close();
            _conn = new SQLiteConnection(dbPath);
            _conn.Execute("PRAGMA journal_mode=WAL");
            _conn.Execute("PRAGMA busy_timeout=5000");
            CreateTables();
            new DatabaseMigrator(_conn).Migrate();
        }

        /// <summary>Execute an action under the database lock (for thread-safe access from background threads).</summary>
        public void RunLocked(Action action)
        {
            lock (_lock) { action(); }
        }

        /// <summary>Execute a function under the database lock and return the result.</summary>
        public T RunLocked<T>(Func<T> func)
        {
            lock (_lock) { return func(); }
        }

        public void Close() { _conn?.Dispose(); _conn = null; }
        public void Dispose() => Close();

        private void CreateTables()
        {
            _conn.Execute(@"CREATE TABLE IF NOT EXISTS resources (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, file_name TEXT NOT NULL,
                relative_path TEXT, original_path TEXT, resource_type INTEGER NOT NULL,
                file_size INTEGER DEFAULT 0, file_hash TEXT, rating INTEGER DEFAULT 0,
                version TEXT DEFAULT '', description TEXT DEFAULT '', source_url TEXT DEFAULT '',
                unity_version_min TEXT DEFAULT '', unity_version_max TEXT DEFAULT '',
                thumbnail_path TEXT DEFAULT '', is_reference INTEGER DEFAULT 0,
                imported_at TEXT NOT NULL, file_modified_at TEXT DEFAULT '',
                folder_id TEXT, FOREIGN KEY (folder_id) REFERENCES folders(id) ON DELETE SET NULL);");

            _conn.Execute(@"CREATE TABLE IF NOT EXISTS folders (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, parent_id TEXT,
                sort_order INTEGER DEFAULT 0, color_tag TEXT DEFAULT '',
                is_smart_folder INTEGER DEFAULT 0, smart_filter TEXT DEFAULT '',
                created_at TEXT NOT NULL, FOREIGN KEY (parent_id) REFERENCES folders(id) ON DELETE CASCADE);");

            _conn.Execute(@"CREATE TABLE IF NOT EXISTS tags (
                id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE,
                group_name TEXT DEFAULT '', color TEXT DEFAULT '#4FC3F7',
                usage_count INTEGER DEFAULT 0);");

            _conn.Execute(@"CREATE TABLE IF NOT EXISTS resource_tags (
                resource_id TEXT NOT NULL, tag_id TEXT NOT NULL,
                PRIMARY KEY (resource_id, tag_id),
                FOREIGN KEY (resource_id) REFERENCES resources(id) ON DELETE CASCADE,
                FOREIGN KEY (tag_id) REFERENCES tags(id) ON DELETE CASCADE);");

            _conn.Execute("CREATE INDEX IF NOT EXISTS idx_res_type ON resources(resource_type);");
            _conn.Execute("CREATE INDEX IF NOT EXISTS idx_res_folder ON resources(folder_id);");
            _conn.Execute("CREATE INDEX IF NOT EXISTS idx_res_rating ON resources(rating);");
            _conn.Execute("CREATE INDEX IF NOT EXISTS idx_res_imported ON resources(imported_at);");

            // FTS5 full-text search (graceful degradation if unavailable)
            try
            {
                _conn.Execute("CREATE VIRTUAL TABLE IF NOT EXISTS resources_fts USING fts5(rid UNINDEXED, name, file_name, description)");
                _fts5Available = true;
            }
            catch { _fts5Available = false; }
        }

        // ==================== Resources ====================

        public void InsertResource(ResourceItem item)
        {
            object folderId = string.IsNullOrEmpty(item.FolderId) ? null : (object)item.FolderId;
            _conn.Execute(@"INSERT INTO resources (id,name,file_name,relative_path,original_path,
                resource_type,file_size,file_hash,rating,version,description,source_url,
                unity_version_min,unity_version_max,thumbnail_path,is_reference,imported_at,
                file_modified_at,folder_id) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                item.Id, item.Name, item.FileName, item.RelativePath, item.OriginalPath,
                (int)item.Type, item.FileSize, item.FileHash, item.Rating, item.Version,
                item.Description, item.SourceUrl, item.UnityVersionMin, item.UnityVersionMax,
                item.ThumbnailPath, item.IsReference ? 1 : 0, item.ImportedAt, item.FileModifiedAt, folderId);
            if (item.TagIds != null)
                foreach (var tagId in item.TagIds)
                    _conn.Execute("INSERT OR IGNORE INTO resource_tags (resource_id,tag_id) VALUES (?,?)", item.Id, tagId);
            if (_fts5Available)
                _conn.Execute("INSERT INTO resources_fts(rid, name, file_name, description) VALUES (?,?,?,?)",
                    item.Id, item.Name, item.FileName, item.Description ?? "");
        }

        public void UpdateResource(ResourceItem item)
        {
            object folderId = string.IsNullOrEmpty(item.FolderId) ? null : (object)item.FolderId;
            _conn.Execute(@"UPDATE resources SET name=?,file_name=?,relative_path=?,original_path=?,
                resource_type=?,file_size=?,file_hash=?,rating=?,version=?,description=?,source_url=?,
                unity_version_min=?,unity_version_max=?,thumbnail_path=?,is_reference=?,
                file_modified_at=?,folder_id=? WHERE id=?",
                item.Name, item.FileName, item.RelativePath, item.OriginalPath,
                (int)item.Type, item.FileSize, item.FileHash, item.Rating, item.Version,
                item.Description, item.SourceUrl, item.UnityVersionMin, item.UnityVersionMax,
                item.ThumbnailPath, item.IsReference ? 1 : 0, item.FileModifiedAt, folderId, item.Id);
            _conn.Execute("DELETE FROM resource_tags WHERE resource_id=?", item.Id);
            if (item.TagIds != null)
                foreach (var tagId in item.TagIds)
                    _conn.Execute("INSERT OR IGNORE INTO resource_tags (resource_id,tag_id) VALUES (?,?)", item.Id, tagId);
            if (_fts5Available)
            {
                _conn.Execute("DELETE FROM resources_fts WHERE rid=?", item.Id);
                _conn.Execute("INSERT INTO resources_fts(rid, name, file_name, description) VALUES (?,?,?,?)",
                    item.Id, item.Name, item.FileName, item.Description ?? "");
            }
        }

        /// <summary>Update resource fields and FTS only, without rewriting tags (for metadata-only edits).</summary>
        public void UpdateResourceMetadata(ResourceItem item)
        {
            object folderId = string.IsNullOrEmpty(item.FolderId) ? null : (object)item.FolderId;
            _conn.Execute(@"UPDATE resources SET name=?,file_name=?,relative_path=?,original_path=?,
                resource_type=?,file_size=?,file_hash=?,rating=?,version=?,description=?,source_url=?,
                unity_version_min=?,unity_version_max=?,thumbnail_path=?,is_reference=?,
                file_modified_at=?,folder_id=? WHERE id=?",
                item.Name, item.FileName, item.RelativePath, item.OriginalPath,
                (int)item.Type, item.FileSize, item.FileHash, item.Rating, item.Version,
                item.Description, item.SourceUrl, item.UnityVersionMin, item.UnityVersionMax,
                item.ThumbnailPath, item.IsReference ? 1 : 0, item.FileModifiedAt, folderId, item.Id);
            if (_fts5Available)
            {
                _conn.Execute("DELETE FROM resources_fts WHERE rid=?", item.Id);
                _conn.Execute("INSERT INTO resources_fts(rid, name, file_name, description) VALUES (?,?,?,?)",
                    item.Id, item.Name, item.FileName, item.Description ?? "");
            }
        }

        public void DeleteResource(string id)
        {
            _conn.Execute("DELETE FROM resource_tags WHERE resource_id=?", id);
            _conn.Execute("DELETE FROM resources WHERE id=?", id);
            if (_fts5Available)
                _conn.Execute("DELETE FROM resources_fts WHERE rid=?", id);
        }

        public ResourceItem GetResource(string id)
        {
            var rows = _conn.Query("SELECT * FROM resources WHERE id=?", id);
            if (rows.Count == 0) return null;
            var item = RowToResource(rows[0]);
            item.TagIds = GetResourceTagIds(id);
            return item;
        }

        public List<ResourceItem> GetAllResources()
        {
            var rows = _conn.Query("SELECT * FROM resources ORDER BY imported_at DESC");
            var items = rows.Select(RowToResource).ToList();
            PopulateTagIds(items);
            return items;
        }

        public List<ResourceItem> GetResourcesByFolder(string folderId)
        {
            var rows = string.IsNullOrEmpty(folderId)
                ? _conn.Query("SELECT * FROM resources WHERE folder_id IS NULL OR folder_id='' ORDER BY imported_at DESC")
                : _conn.Query("SELECT * FROM resources WHERE folder_id=? ORDER BY imported_at DESC", folderId);
            var items = rows.Select(RowToResource).ToList();
            PopulateTagIds(items);
            return items;
        }

        public List<ResourceItem> SearchResources(SearchQuery query)
        {
            var conds = new List<string>(); var parms = new List<object>();
            if (!string.IsNullOrEmpty(query.Keyword))
            {
                if (_fts5Available)
                {
                    conds.Add("id IN (SELECT rid FROM resources_fts WHERE resources_fts MATCH ?)");
                    parms.Add(EscapeFts5Query(query.Keyword));
                }
                else
                {
                    conds.Add("(name LIKE ? OR file_name LIKE ? OR description LIKE ?)");
                    string kw = $"%{query.Keyword}%";
                    parms.AddRange(new object[] { kw, kw, kw });
                }
            }
            if (query.TypeFilter.HasValue) { conds.Add("resource_type=?"); parms.Add((int)query.TypeFilter.Value); }
            if (!string.IsNullOrEmpty(query.FolderId)) { conds.Add("folder_id=?"); parms.Add(query.FolderId); }
            if (query.MinRating.HasValue) { conds.Add("rating>=?"); parms.Add(query.MinRating.Value); }
            if (query.MinSize.HasValue) { conds.Add("file_size>=?"); parms.Add(query.MinSize.Value); }
            if (query.MaxSize.HasValue) { conds.Add("file_size<=?"); parms.Add(query.MaxSize.Value); }
            if (query.TagIds != null && query.TagIds.Count > 0)
            {
                for (int i = 0; i < query.TagIds.Count; i++)
                {
                    conds.Add($"EXISTS (SELECT 1 FROM resource_tags WHERE resource_id=resources.id AND tag_id=?)");
                    parms.Add(query.TagIds[i]);
                }
            }

            string where = conds.Count > 0 ? "WHERE " + string.Join(" AND ", conds) : "";
            string sort = SanitizeSort(query.SortField);
            string sql = $"SELECT * FROM resources {where} ORDER BY {sort} {(query.SortDescending ? "DESC" : "ASC")} LIMIT ? OFFSET ?";
            parms.Add(query.Limit); parms.Add(query.Offset);

            var rows = _conn.Query(sql, parms.ToArray());
            var items = rows.Select(RowToResource).ToList();
            PopulateTagIds(items);
            return items;
        }

        public int GetResourceCount() => (int)_conn.Scalar<long>("SELECT COUNT(*) FROM resources");
        public bool ResourceExistsByHash(string hash) => _conn.Scalar<long>("SELECT COUNT(*) FROM resources WHERE file_hash=?", hash) > 0;

        // ==================== Folders ====================

        public void InsertFolder(ResourceFolder f)
        {
            _conn.Execute("INSERT INTO folders (id,name,parent_id,sort_order,color_tag,is_smart_folder,smart_filter,created_at) VALUES (?,?,?,?,?,?,?,?)",
                f.Id, f.Name, f.ParentId, f.SortOrder, f.ColorTag, f.IsSmartFolder ? 1 : 0, f.SmartFilter, f.CreatedAt);
        }

        public void UpdateFolder(ResourceFolder f)
        {
            _conn.Execute("UPDATE folders SET name=?,parent_id=?,sort_order=?,color_tag=?,is_smart_folder=?,smart_filter=? WHERE id=?",
                f.Name, f.ParentId, f.SortOrder, f.ColorTag, f.IsSmartFolder ? 1 : 0, f.SmartFilter, f.Id);
        }

        /// <summary>Move multiple resources to a folder in a single SQL UPDATE.</summary>
        public void MoveResourcesToFolder(List<string> resourceIds, string folderId)
        {
            if (resourceIds == null || resourceIds.Count == 0) return;
            object fid = string.IsNullOrEmpty(folderId) ? null : (object)folderId;
            const int chunkSize = 500;
            for (int offset = 0; offset < resourceIds.Count; offset += chunkSize)
            {
                var chunk = resourceIds.Skip(offset).Take(chunkSize).ToList();
                var placeholders = string.Join(",", chunk.Select(_ => "?"));
                var parms = new List<object> { fid };
                parms.AddRange(chunk.Cast<object>());
                _conn.Execute($"UPDATE resources SET folder_id=? WHERE id IN ({placeholders})", parms.ToArray());
            }
        }

        public void DeleteFolder(string id)
        {
            _conn.Execute("UPDATE resources SET folder_id=NULL WHERE folder_id=?", id);
            _conn.Execute("UPDATE folders SET parent_id=NULL WHERE parent_id=?", id);
            _conn.Execute("DELETE FROM folders WHERE id=?", id);
        }

        public ResourceFolder GetFolder(string id)
        {
            var rows = _conn.Query("SELECT * FROM folders WHERE id=?", id);
            return rows.Count > 0 ? RowToFolder(rows[0]) : null;
        }

        public List<ResourceFolder> GetAllFolders() =>
            _conn.Query("SELECT * FROM folders ORDER BY sort_order,name").Select(RowToFolder).ToList();

        public List<ResourceFolder> GetChildFolders(string parentId)
        {
            var rows = string.IsNullOrEmpty(parentId)
                ? _conn.Query("SELECT * FROM folders WHERE parent_id IS NULL OR parent_id='' ORDER BY sort_order,name")
                : _conn.Query("SELECT * FROM folders WHERE parent_id=? ORDER BY sort_order,name", parentId);
            return rows.Select(RowToFolder).ToList();
        }

        // ==================== Tags ====================

        public void InsertTag(ResourceTag t) =>
            _conn.Execute("INSERT INTO tags (id,name,group_name,color,usage_count) VALUES (?,?,?,?,?)", t.Id, t.Name, t.GroupName, t.Color, t.UsageCount);

        public void UpdateTag(ResourceTag t) =>
            _conn.Execute("UPDATE tags SET name=?,group_name=?,color=? WHERE id=?", t.Name, t.GroupName, t.Color, t.Id);

        public void DeleteTag(string id) { _conn.Execute("DELETE FROM resource_tags WHERE tag_id=?", id); _conn.Execute("DELETE FROM tags WHERE id=?", id); }

        public List<ResourceTag> GetAllTags() =>
            _conn.Query("SELECT * FROM tags ORDER BY group_name,name").Select(RowToTag).ToList();

        public void AddTagToResource(string rid, string tid) { _conn.Execute("INSERT OR IGNORE INTO resource_tags VALUES (?,?)", rid, tid); RefreshTagCount(tid); }
        public void AddTagToResourceNoCount(string rid, string tid) { _conn.Execute("INSERT OR IGNORE INTO resource_tags VALUES (?,?)", rid, tid); }
        public void RemoveTagFromResource(string rid, string tid) { _conn.Execute("DELETE FROM resource_tags WHERE resource_id=? AND tag_id=?", rid, tid); RefreshTagCount(tid); }
        public void RemoveTagFromResourceNoCount(string rid, string tid) { _conn.Execute("DELETE FROM resource_tags WHERE resource_id=? AND tag_id=?", rid, tid); }
        public List<string> GetResourceTagIds(string rid) => _conn.Query("SELECT tag_id FROM resource_tags WHERE resource_id=?", rid).Select(r => r["tag_id"]?.ToString() ?? "").Where(s => s != "").ToList();

        /// <summary>
        /// Batch-load TagIds for a list of resources in a single query, eliminating N+1.
        /// Splits into chunks of 500 to stay within SQLite variable limits.
        /// </summary>
        private void PopulateTagIds(List<ResourceItem> items)
        {
            if (items == null || items.Count == 0) return;

            var lookup = new Dictionary<string, ResourceItem>(items.Count);
            foreach (var item in items)
            {
                item.TagIds = new List<string>();
                lookup[item.Id] = item;
            }

            const int chunkSize = 500;
            var ids = items.Select(it => it.Id).ToList();
            for (int offset = 0; offset < ids.Count; offset += chunkSize)
            {
                var chunk = ids.Skip(offset).Take(chunkSize).ToList();
                var placeholders = string.Join(",", chunk.Select(_ => "?"));
                var sql = $"SELECT resource_id, tag_id FROM resource_tags WHERE resource_id IN ({placeholders})";
                var rows = _conn.Query(sql, chunk.Cast<object>().ToArray());
                foreach (var row in rows)
                {
                    var rid = row["resource_id"]?.ToString() ?? "";
                    var tid = row["tag_id"]?.ToString() ?? "";
                    if (rid != "" && tid != "" && lookup.TryGetValue(rid, out var item))
                        item.TagIds.Add(tid);
                }
            }
        }
        public void RefreshTagCount(string tid) => _conn.Execute("UPDATE tags SET usage_count=(SELECT COUNT(*) FROM resource_tags WHERE tag_id=?) WHERE id=?", tid, tid);

        // ==================== Row Mappers ====================

        private string SanitizeSort(string f) { switch (f) { case "name": case "file_name": case "file_size": case "rating": case "resource_type": case "file_modified_at": return f; default: return "imported_at"; } }

        /// <summary>Rebuild the FTS5 index from all resources.</summary>
        public void RebuildFTS()
        {
            if (!_fts5Available) return;
            _conn.Execute("DELETE FROM resources_fts");
            _conn.Execute("INSERT INTO resources_fts(rid, name, file_name, description) SELECT id, name, file_name, description FROM resources");
        }

        private string EscapeFts5Query(string input)
        {
            var words = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var parts = new List<string>();
            foreach (var word in words)
                parts.Add("\"" + word.Replace("\"", "\"\"") + "\"*");
            return parts.Count > 0 ? string.Join(" ", parts) : "\"\"";
        }

        private static ResourceItem RowToResource(Dictionary<string, object> r) => new ResourceItem
        {
            Id = r["id"]?.ToString() ?? "", Name = r["name"]?.ToString() ?? "", FileName = r["file_name"]?.ToString() ?? "",
            RelativePath = r["relative_path"]?.ToString() ?? "", OriginalPath = r["original_path"]?.ToString() ?? "",
            Type = (ResourceType)(int)(long)(r["resource_type"] ?? (long)99), FileSize = r["file_size"] is long l ? l : 0,
            FileHash = r["file_hash"]?.ToString() ?? "", Rating = r["rating"] is long rv ? (int)rv : 0,
            Version = r["version"]?.ToString() ?? "", Description = r["description"]?.ToString() ?? "",
            SourceUrl = r["source_url"]?.ToString() ?? "", UnityVersionMin = r["unity_version_min"]?.ToString() ?? "",
            UnityVersionMax = r["unity_version_max"]?.ToString() ?? "", ThumbnailPath = r["thumbnail_path"]?.ToString() ?? "",
            IsReference = r["is_reference"] is long ir && ir != 0, ImportedAt = r["imported_at"]?.ToString() ?? "",
            FileModifiedAt = r["file_modified_at"]?.ToString() ?? "", FolderId = r["folder_id"]?.ToString() ?? ""
        };

        private static ResourceFolder RowToFolder(Dictionary<string, object> r) => new ResourceFolder
        {
            Id = r["id"]?.ToString() ?? "", Name = r["name"]?.ToString() ?? "", ParentId = r["parent_id"]?.ToString() ?? "",
            SortOrder = r["sort_order"] is long s ? (int)s : 0, ColorTag = r["color_tag"]?.ToString() ?? "",
            IsSmartFolder = r["is_smart_folder"] is long isf && isf != 0, SmartFilter = r["smart_filter"]?.ToString() ?? "",
            CreatedAt = r["created_at"]?.ToString() ?? ""
        };

        private static ResourceTag RowToTag(Dictionary<string, object> r) => new ResourceTag
        {
            Id = r["id"]?.ToString() ?? "", Name = r["name"]?.ToString() ?? "", GroupName = r["group_name"]?.ToString() ?? "",
            Color = r["color"]?.ToString() ?? "#4FC3F7", UsageCount = r["usage_count"] is long uc ? (int)uc : 0
        };
    }
}
