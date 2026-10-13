using System;
using System.Collections.Generic;
using MagicBox.Core;
using MagicBox.Models;
using MagicBox.UI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicBox.UI.Views
{
    public class ContentListView
    {
        public VisualElement Root { get; private set; }

        public event Action<ResourceItem> OnSelected;
        /// <summary>Click with index and modifier keys for multi-select.</summary>
        public event Action<ResourceItem, int, bool, bool> OnItemClicked;
        public event Action<ResourceItem> OnDoubleClicked;
        public event Action<ResourceItem, Vector2> OnRightClicked;

        private VisualElement _listContainer;
        private ScrollView _scroll;
        private HashSet<string> _selectedIds = new HashSet<string>();

        // Virtualization
        private List<ResourceItem> _allItems;
        private VisualElement _topSpacer;
        private VisualElement _bottomSpacer;
        private const int RowHeight = 30;
        private const int BufferRows = 10;
        private int _renderedFirst = -1;
        private int _renderedLast = -1;

        public VisualElement Build()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;
            Root.style.display = DisplayStyle.None;

            // Header
            var header = new VisualElement();
            header.AddToClassList("mb-list-header");
            Root.Add(header);
            AddHCell(header, "", 28);
            AddHCell(header, Loc.T("list.name"), 0, true);
            AddHCell(header, Loc.T("list.type"), 65);
            AddHCell(header, Loc.T("list.size"), 80);
            AddHCell(header, Loc.T("list.date"), 95);
            AddHCell(header, Loc.T("list.rating"), 65);

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            _scroll.verticalScroller.valueChanged += _ => RefreshVisibleRows();
            _scroll.RegisterCallback<GeometryChangedEvent>(_ => RefreshVisibleRows());
            Root.Add(_scroll);

            _topSpacer = new VisualElement();
            _scroll.Add(_topSpacer);

            _listContainer = new VisualElement();
            _scroll.Add(_listContainer);

            _bottomSpacer = new VisualElement();
            _scroll.Add(_bottomSpacer);

            return Root;
        }

        private void AddHCell(VisualElement parent, string text, float w, bool flex = false)
        {
            var c = new Label(text); c.AddToClassList("mb-list-hcell");
            if (flex) c.style.flexGrow = 1; else c.style.width = w;
            parent.Add(c);
        }

        public void SetItems(List<ResourceItem> items)
        {
            _allItems = items;
            _renderedFirst = -1;
            _renderedLast = -1;
            _listContainer.Clear();
            _topSpacer.style.height = 0;
            _bottomSpacer.style.height = 0;

            if (items == null || items.Count == 0) return;

            // Trigger initial render
            RefreshVisibleRows();
        }

        /// <summary>Re-render visible rows without heavy reset (e.g. after metadata edit).</summary>
        public void InvalidateRows()
        {
            _renderedFirst = -1;
            _renderedLast = -1;
            _listContainer.Clear();
            RefreshVisibleRows();
        }

        private void RefreshVisibleRows()
        {
            if (_allItems == null || _allItems.Count == 0) return;

            float scrollY = _scroll.scrollOffset.y;
            float viewHeight = _scroll.resolvedStyle.height;
            if (viewHeight <= 0) viewHeight = 600;

            int totalItems = _allItems.Count;
            int firstVisible = Math.Max(0, (int)(scrollY / RowHeight) - BufferRows);
            int visibleCount = (int)(viewHeight / RowHeight) + BufferRows * 2 + 1;
            int lastVisible = Math.Min(totalItems - 1, firstVisible + visibleCount);

            // Skip if same range
            if (firstVisible == _renderedFirst && lastVisible == _renderedLast) return;

            _renderedFirst = firstVisible;
            _renderedLast = lastVisible;

            _listContainer.Clear();
            _topSpacer.style.height = firstVisible * RowHeight;
            _bottomSpacer.style.height = Math.Max(0, (totalItems - 1 - lastVisible) * RowHeight);

            for (int i = firstVisible; i <= lastVisible; i++)
                _listContainer.Add(MakeRow(_allItems[i], i));
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
            _listContainer.Query<VisualElement>(className: "mb-list-row").ForEach(el =>
            {
                var it = el.userData as ResourceItem;
                el.EnableInClassList("mb-list-row--selected", it != null && _selectedIds.Contains(it.Id));
            });
        }

        private VisualElement MakeRow(ResourceItem item, int index)
        {
            var row = new VisualElement();
            row.AddToClassList("mb-list-row");
            row.style.height = RowHeight;
            row.userData = item;

            if (_selectedIds.Contains(item.Id))
                row.AddToClassList("mb-list-row--selected");

            row.RegisterCallback<ClickEvent>(e =>
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
            row.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1)
                {
                    if (!_selectedIds.Contains(item.Id))
                        OnSelected?.Invoke(item);
                    OnRightClicked?.Invoke(item, e.position);
                    e.StopPropagation();
                    e.PreventDefault();
                }
            });

            var iconWrap = new VisualElement();
            iconWrap.style.width = 28;
            iconWrap.style.alignItems = Align.Center;
            iconWrap.style.justifyContent = Justify.Center;
            iconWrap.Add(FileIconGenerator.CreateSmall(item));
            row.Add(iconWrap);

            var name = new Label(item.Name);
            name.AddToClassList("mb-list-cell"); name.style.flexGrow = 1;
            name.tooltip = item.FileName;
            row.Add(name);

            var type = new Label(ContentGridView.GetTypeLabel(item.Type));
            type.AddToClassList("mb-list-cell"); type.style.width = 65;
            type.style.color = ContentGridView.GetTypeColor(item.Type);
            row.Add(type);

            var size = new Label(item.FileSizeDisplay);
            size.AddToClassList("mb-list-cell"); size.style.width = 80;
            row.Add(size);

            string dateStr = "";
            if (DateTime.TryParse(item.ImportedAt, out DateTime dt)) dateStr = dt.ToString("yyyy-MM-dd");
            var date = new Label(dateStr);
            date.AddToClassList("mb-list-cell"); date.style.width = 95;
            row.Add(date);

            string starsStr = item.Rating > 0 ? new string('\u2605', item.Rating) : "-";
            var rating = new Label(starsStr);
            rating.AddToClassList("mb-list-cell"); rating.style.width = 65;
            if (item.Rating > 0) rating.style.color = new Color(1f, 0.84f, 0f);
            row.Add(rating);

            return row;
        }
    }
}
