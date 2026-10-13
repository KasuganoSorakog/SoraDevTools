using System;
using System.Collections.Generic;
using System.IO;
using MagicBox.Core;
using MagicBox.Models;
using MagicBox.Services;
using MagicBox.UI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicBox.UI.Views
{
    public class ContentGridView
    {
        public VisualElement Root { get; private set; }

        public event Action<ResourceItem> OnSelected;
        /// <summary>Click with index and modifier keys for multi-select.</summary>
        public event Action<ResourceItem, int, bool, bool> OnItemClicked;
        public event Action<ResourceItem> OnDoubleClicked;
        public event Action<ResourceItem, Vector2> OnRightClicked;

        private VisualElement _grid;
        private VisualElement _empty;
        private ScrollView _scroll;
        private HashSet<string> _selectedIds = new HashSet<string>();

        // Thumbnail texture cache: keyed by ThumbnailPath, survives scroll re-renders
        private Dictionary<string, Texture2D> _thumbCache = new Dictionary<string, Texture2D>();

        // Virtualization
        private List<ResourceItem> _allItems;
        private VisualElement _topSpacer;
        private VisualElement _bottomSpacer;
        private const int CardHeight = 170;
        private const int CardWidth = 140;
        private const int BufferRows = 3;
        private int _columnsPerRow = 4;
        public int ColumnsPerRow => _columnsPerRow;
        private int _renderedFirst = -1;
        private int _renderedLast = -1;

        public VisualElement Build()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("mb-grid-scroll");
            _scroll.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _scroll.verticalScroller.valueChanged += _ => RefreshVisibleCards();
            Root.Add(_scroll);

            _topSpacer = new VisualElement();
            _topSpacer.style.flexShrink = 0;
            _scroll.Add(_topSpacer);

            _grid = new VisualElement();
            _grid.AddToClassList("mb-grid");
            _scroll.Add(_grid);

            _bottomSpacer = new VisualElement();
            _bottomSpacer.style.flexShrink = 0;
            _scroll.Add(_bottomSpacer);

            _empty = new VisualElement();
            _empty.AddToClassList("mb-empty");
            _empty.style.display = DisplayStyle.None;
            Root.Add(_empty);

            var emptyText = new Label(Loc.T("content.empty"));
            emptyText.AddToClassList("mb-empty-text");
            _empty.Add(emptyText);

            return Root;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            float w = _scroll.contentContainer.resolvedStyle.width;
            if (w > 0)
            {
                int newCols = Math.Max(1, (int)(w / CardWidth));
                if (newCols != _columnsPerRow)
                {
                    _columnsPerRow = newCols;
                    _renderedFirst = -1;
                    _renderedLast = -1;
                    RefreshVisibleCards();
                }
            }
        }

        public void SetItems(List<ResourceItem> items)
        {
            ClearThumbCache();
            _grid.Clear();
            _allItems = items;
            _renderedFirst = -1;
            _renderedLast = -1;
            _topSpacer.style.height = 0;
            _bottomSpacer.style.height = 0;

            bool has = items != null && items.Count > 0;
            _scroll.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;

            if (!has) return;
            RefreshVisibleCards();
        }

        /// <summary>Re-render visible cards without clearing the thumbnail cache (e.g. after metadata edit).</summary>
        public void InvalidateCards()
        {
            _renderedFirst = -1;
            _renderedLast = -1;
            _grid.Clear();
            RefreshVisibleCards();
        }

        private void RefreshVisibleCards()
        {
            if (_allItems == null || _allItems.Count == 0) return;

            int cols = _columnsPerRow;
            int totalRows = (_allItems.Count + cols - 1) / cols;
            float scrollY = _scroll.scrollOffset.y;
            float viewHeight = _scroll.resolvedStyle.height;
            if (viewHeight <= 0) viewHeight = 600;

            int firstVisibleRow = Math.Max(0, (int)(scrollY / CardHeight) - BufferRows);
            int visibleRowCount = (int)(viewHeight / CardHeight) + BufferRows * 2 + 1;
            int lastVisibleRow = Math.Min(totalRows - 1, firstVisibleRow + visibleRowCount);

            int firstItem = firstVisibleRow * cols;
            int lastItem = Math.Min(_allItems.Count - 1, (lastVisibleRow + 1) * cols - 1);

            if (firstItem == _renderedFirst && lastItem == _renderedLast) return;

            _renderedFirst = firstItem;
            _renderedLast = lastItem;

            _grid.Clear();

            _topSpacer.style.height = firstVisibleRow * CardHeight;
            _bottomSpacer.style.height = Math.Max(0, (totalRows - 1 - lastVisibleRow) * CardHeight);

            for (int i = firstItem; i <= lastItem; i++)
                _grid.Add(MakeCard(_allItems[i], i));
        }

        /// <summary>Evict a specific thumbnail from cache (e.g. after cover image replacement).</summary>
        public void EvictThumb(string thumbPath)
        {
            if (!string.IsNullOrEmpty(thumbPath) && _thumbCache.TryGetValue(thumbPath, out var tex))
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                _thumbCache.Remove(thumbPath);
            }
        }

        private void ClearThumbCache()
        {
            foreach (var tex in _thumbCache.Values)
                if (tex != null) UnityEngine.Object.Destroy(tex);
            _thumbCache.Clear();
        }

        public void SetSelected(string id)
        {
            _selectedIds.Clear();
            if (id != null) _selectedIds.Add(id);
            ApplySelectionStyles();
        }

        public void SetSelectedIds(HashSet<string> ids)
        {
            _selectedIds = ids ?? new HashSet<string>();
            ApplySelectionStyles();
        }

        private void ApplySelectionStyles()
        {
            _grid.Query<VisualElement>(className: "mb-card").ForEach(el =>
            {
                var it = el.userData as ResourceItem;
                el.EnableInClassList("mb-card--selected", it != null && _selectedIds.Contains(it.Id));
            });
        }

        private VisualElement MakeCard(ResourceItem item, int index)
        {
            var card = new VisualElement();
            card.AddToClassList("mb-card");
            card.userData = item;

            if (_selectedIds.Contains(item.Id))
                card.AddToClassList("mb-card--selected");

            card.RegisterCallback<ClickEvent>(e =>
            {
                if (e.clickCount >= 2) { OnDoubleClicked?.Invoke(item); }
                else
                {
                    bool ctrl = e.ctrlKey;
                    bool shift = e.shiftKey;
                    if (ctrl || shift)
                        OnItemClicked?.Invoke(item, index, ctrl, shift);
                    else
                        OnSelected?.Invoke(item);
                }
                e.StopPropagation();
            });
            card.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1)
                {
                    // If right-clicking an unselected item, select it first
                    if (!_selectedIds.Contains(item.Id))
                        OnSelected?.Invoke(item);
                    OnRightClicked?.Invoke(item, e.position);
                    e.StopPropagation();
                    e.PreventDefault();
                }
            });

            // Thumb area
            var thumb = new VisualElement();
            thumb.AddToClassList("mb-card-thumb");
            card.Add(thumb);

            bool hasCover = false;
            if (!string.IsNullOrEmpty(item.ThumbnailPath))
            {
                // Try cached texture first, then load from disk
                if (_thumbCache.TryGetValue(item.ThumbnailPath, out var cached))
                {
                    thumb.style.backgroundImage = new StyleBackground(cached);
                    #pragma warning disable CS0618
                    thumb.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                    #pragma warning restore CS0618
                    hasCover = true;
                }
                else
                {
                    string thumbDir = LibraryManager.Instance.GetThumbnailsDirectory();
                    if (!string.IsNullOrEmpty(thumbDir))
                    {
                        string coverPath = Path.Combine(thumbDir, item.ThumbnailPath);
                        if (File.Exists(coverPath))
                        {
                            try
                            {
                                byte[] bytes = File.ReadAllBytes(coverPath);
                                var tex = new Texture2D(2, 2);
                                if (tex.LoadImage(bytes))
                                {
                                    _thumbCache[item.ThumbnailPath] = tex;
                                    thumb.style.backgroundImage = new StyleBackground(tex);
                                    #pragma warning disable CS0618
                                    thumb.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                                    #pragma warning restore CS0618
                                    hasCover = true;
                                }
                                else
                                {
                                    UnityEngine.Object.Destroy(tex);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[MagicBox] Load cover failed for '{item.Name}': {ex.Message}");
                            }
                        }
                    }
                }
            }

            if (!hasCover)
            {
                var icon = FileIconGenerator.CreateLarge(item);
                thumb.Add(icon);
            }

            var badge = new Label(GetTypeLabel(item.Type));
            badge.AddToClassList("mb-card-badge");
            badge.style.backgroundColor = GetTypeColor(item.Type);
            thumb.Add(badge);

            if (item.Rating > 0)
            {
                var stars = new Label(new string('\u2605', item.Rating));
                stars.AddToClassList("mb-card-stars");
                thumb.Add(stars);
            }

            // Info
            var info = new VisualElement();
            info.AddToClassList("mb-card-info");
            card.Add(info);

            var name = new Label(item.Name);
            name.AddToClassList("mb-card-name");
            name.tooltip = item.FileName;
            info.Add(name);

            var size = new Label(item.FileSizeDisplay);
            size.AddToClassList("mb-card-size");
            info.Add(size);

            return card;
        }

        public static string GetTypeIcon(ResourceType t)
        {
            switch (t)
            {
                case ResourceType.UnityPackage: return "\u25A3";
                case ResourceType.ZipArchive: return "\u2702";
                case ResourceType.CSharpScript: return "\u2630";
                case ResourceType.ShaderFile: return "\u2726";
                case ResourceType.Prefab: return "\u2B22";
                case ResourceType.Texture: return "\u25A8";
                case ResourceType.Model3D: return "\u2B21";
                case ResourceType.Audio: return "\u266B";
                case ResourceType.Font: return "Aa";
                case ResourceType.Document: return "\u2637";
                case ResourceType.Executable: return "\u2699";
                case ResourceType.Folder: return "\u25A1";
                default: return "\u25CB";
            }
        }

        public static string GetTypeLabel(ResourceType t)
        {
            switch (t)
            {
                case ResourceType.UnityPackage: return "PKG";
                case ResourceType.ZipArchive: return "ZIP";
                case ResourceType.CSharpScript: return "C#";
                case ResourceType.ShaderFile: return "HLSL";
                case ResourceType.Prefab: return "PRE";
                case ResourceType.Texture: return "TEX";
                case ResourceType.Model3D: return "3D";
                case ResourceType.Audio: return "SFX";
                case ResourceType.Font: return "FONT";
                case ResourceType.Document: return "DOC";
                case ResourceType.Executable: return "EXE";
                case ResourceType.Folder: return "DIR";
                default: return "FILE";
            }
        }

        public static Color GetTypeColor(ResourceType t)
        {
            switch (t)
            {
                case ResourceType.UnityPackage: return new Color(0.4f, 0.73f, 0.42f);
                case ResourceType.ZipArchive: return new Color(1f, 0.65f, 0.15f);
                case ResourceType.CSharpScript: return new Color(0.26f, 0.65f, 0.96f);
                case ResourceType.ShaderFile: return new Color(0.67f, 0.28f, 0.74f);
                case ResourceType.Texture: return new Color(0.94f, 0.33f, 0.31f);
                case ResourceType.Model3D: return new Color(0.93f, 0.76f, 0.26f);
                case ResourceType.Audio: return new Color(0.08f, 0.73f, 0.69f);
                case ResourceType.Font: return new Color(0.47f, 0.56f, 0.61f);
                case ResourceType.Executable: return new Color(0.85f, 0.44f, 0.44f);
                default: return new Color(0.4f, 0.5f, 0.6f);
            }
        }
    }
}
