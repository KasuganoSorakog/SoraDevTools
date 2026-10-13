using System;
using System.Collections.Generic;
using System.Linq;
using MagicBox.Models;
using MagicBox.Services;

namespace MagicBox.UI
{
    /// <summary>
    /// Manages content data state and selection logic, decoupled from UI.
    /// MagicBoxApp drives UI updates after calling ViewModel methods.
    /// </summary>
    public class ContentViewModel
    {
        private readonly ServiceContainer _services;
        private Stack<UndoAction> _undoStack = new Stack<UndoAction>();
        private const int MaxUndoDepth = 20;

        public List<ResourceItem> CurrentItems { get; private set; } = new List<ResourceItem>();
        public ResourceItem SelectedItem { get; private set; }
        public HashSet<string> SelectedIds { get; } = new HashSet<string>();
        public int LastClickIndex { get; private set; } = -1;
        public string CurrentFolderId { get; private set; }
        public string CurrentTagFilter { get; private set; }
        public bool HasUndo => _undoStack.Count > 0;

        public ContentViewModel(ServiceContainer services)
        {
            _services = services;
        }

        // ============ Content Loading ============

        public void LoadResources()
        {
            if (!string.IsNullOrEmpty(CurrentTagFilter))
                CurrentItems = _services.Search.GetByTag(CurrentTagFilter);
            else if (CurrentFolderId != null)
                CurrentItems = _services.Search.GetByFolder(CurrentFolderId);
            else
                CurrentItems = _services.Search.GetAll();
            LastClickIndex = -1;
            ValidateSelection();
        }

        public void SearchByKeyword(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
                LoadResources();
            else
            {
                CurrentItems = _services.Search.QuickSearch(keyword);
                LastClickIndex = -1;
            }
        }

        public void SortBy(string field, bool descending)
        {
            var q = new SearchQuery
            {
                FolderId = CurrentFolderId,
                SortField = field,
                SortDescending = descending,
                Limit = 10000
            };
            if (!string.IsNullOrEmpty(CurrentTagFilter))
                q.TagIds = new List<string> { CurrentTagFilter };
            CurrentItems = _services.Search.Search(q);
            LastClickIndex = -1;
        }

        public void ShowFolder(string folderId)
        {
            CurrentFolderId = folderId;
            CurrentTagFilter = null;
            LoadResources();
        }

        public void ShowAll()
        {
            CurrentFolderId = null;
            CurrentTagFilter = null;
            LoadResources();
        }

        public void ShowTag(string tagId)
        {
            CurrentFolderId = null;
            CurrentTagFilter = tagId;
            LoadResources();
        }

        // ============ Selection ============

        public void HandleItemClick(ResourceItem item, int index, bool ctrl, bool shift)
        {
            if (shift && LastClickIndex >= 0 && LastClickIndex < CurrentItems.Count)
            {
                int from = Math.Min(LastClickIndex, index);
                int to = Math.Max(LastClickIndex, index);
                if (!ctrl) SelectedIds.Clear();
                for (int i = from; i <= to; i++)
                    SelectedIds.Add(CurrentItems[i].Id);
            }
            else if (ctrl)
            {
                if (SelectedIds.Contains(item.Id))
                    SelectedIds.Remove(item.Id);
                else
                    SelectedIds.Add(item.Id);
            }
            else
            {
                SelectedIds.Clear();
                SelectedIds.Add(item.Id);
            }

            LastClickIndex = index;
            SelectedItem = item;
        }

        public void SelectSingle(ResourceItem item)
        {
            SelectedItem = item;
            SelectedIds.Clear();
            if (item != null) SelectedIds.Add(item.Id);
        }

        public void SelectItemAtIndex(int index, bool extendSelection)
        {
            if (index < 0 || index >= CurrentItems.Count) return;

            var item = CurrentItems[index];

            if (extendSelection && LastClickIndex >= 0)
            {
                int from = Math.Min(LastClickIndex, index);
                int to = Math.Max(LastClickIndex, index);
                SelectedIds.Clear();
                for (int i = from; i <= to; i++)
                    SelectedIds.Add(CurrentItems[i].Id);
            }
            else
            {
                SelectedIds.Clear();
                SelectedIds.Add(item.Id);
            }

            LastClickIndex = index;
            SelectedItem = item;
        }

        /// <summary>Navigate by delta (e.g. -1, +1, -cols, +cols). MagicBoxApp computes delta from KeyCode and view mode.</summary>
        public void Navigate(int delta, bool shift)
        {
            if (CurrentItems.Count == 0) return;

            int current = LastClickIndex >= 0 && LastClickIndex < CurrentItems.Count
                ? LastClickIndex : -1;
            int next = Math.Max(0, Math.Min(CurrentItems.Count - 1, current + delta));
            if (next == current && current >= 0) return;
            SelectItemAtIndex(next, shift);
        }

        public void SelectAll()
        {
            SelectedIds.Clear();
            foreach (var item in CurrentItems)
                SelectedIds.Add(item.Id);
        }

        public void ClearSelection()
        {
            SelectedIds.Clear();
            SelectedItem = null;
            LastClickIndex = -1;
        }

        public List<ResourceItem> GetSelectedItems()
        {
            return CurrentItems.Where(it => SelectedIds.Contains(it.Id)).ToList();
        }

        /// <summary>Remove stale selections that no longer exist in CurrentItems.</summary>
        private void ValidateSelection()
        {
            if (SelectedItem == null && SelectedIds.Count == 0) return;
            var currentIds = new HashSet<string>(CurrentItems.Select(it => it.Id));
            if (SelectedItem != null && !currentIds.Contains(SelectedItem.Id))
                SelectedItem = null;
            SelectedIds.IntersectWith(currentIds);
        }

        // ============ Undo ============

        public void DeleteWithUndo(List<ResourceItem> items)
        {
            var snapshots = items.Select(it => new ResourceItemSnapshot(it)).ToList();
            PushUndo(new UndoAction(() =>
            {
                foreach (var s in snapshots)
                    _services.Library.Database.InsertResource(s.ToResourceItem());
            }));

            foreach (var item in items)
                _services.Library.Database.DeleteResource(item.Id);

            ClearSelection();
            LoadResources();
        }

        public void PerformUndo()
        {
            if (_undoStack.Count == 0) return;
            _undoStack.Pop().Undo();
            LoadResources();
        }

        private void PushUndo(UndoAction action)
        {
            _undoStack.Push(action);
            if (_undoStack.Count > MaxUndoDepth)
            {
                var temp = _undoStack.ToArray();
                _undoStack.Clear();
                // temp[0]=newest … temp[N]=oldest; keep the newest MaxUndoDepth items
                for (int i = MaxUndoDepth - 1; i >= 0; i--)
                    _undoStack.Push(temp[i]);
            }
        }
    }

    // ============ Undo Infrastructure ============

    internal class UndoAction
    {
        private readonly Action _undoFn;
        public UndoAction(Action undoFn) { _undoFn = undoFn; }
        public void Undo() => _undoFn?.Invoke();
    }

    internal class ResourceItemSnapshot
    {
        public string Id, Name, FileName, RelativePath, OriginalPath;
        public ResourceType Type; public long FileSize; public string FileHash;
        public int Rating; public string Version, Description, SourceUrl;
        public string UnityVersionMin, UnityVersionMax, ThumbnailPath;
        public bool IsReference; public string ImportedAt, FileModifiedAt, FolderId;
        public List<string> TagIds;

        public ResourceItemSnapshot(ResourceItem item)
        {
            Id = item.Id; Name = item.Name; FileName = item.FileName;
            RelativePath = item.RelativePath; OriginalPath = item.OriginalPath;
            Type = item.Type; FileSize = item.FileSize; FileHash = item.FileHash;
            Rating = item.Rating; Version = item.Version; Description = item.Description;
            SourceUrl = item.SourceUrl; UnityVersionMin = item.UnityVersionMin;
            UnityVersionMax = item.UnityVersionMax; ThumbnailPath = item.ThumbnailPath;
            IsReference = item.IsReference; ImportedAt = item.ImportedAt;
            FileModifiedAt = item.FileModifiedAt; FolderId = item.FolderId;
            TagIds = item.TagIds != null ? new List<string>(item.TagIds) : new List<string>();
        }

        public ResourceItem ToResourceItem() => new ResourceItem
        {
            Id = Id, Name = Name, FileName = FileName, RelativePath = RelativePath,
            OriginalPath = OriginalPath, Type = Type, FileSize = FileSize, FileHash = FileHash,
            Rating = Rating, Version = Version, Description = Description, SourceUrl = SourceUrl,
            UnityVersionMin = UnityVersionMin, UnityVersionMax = UnityVersionMax,
            ThumbnailPath = ThumbnailPath, IsReference = IsReference, ImportedAt = ImportedAt,
            FileModifiedAt = FileModifiedAt, FolderId = FolderId,
            TagIds = new List<string>(TagIds)
        };
    }
}
