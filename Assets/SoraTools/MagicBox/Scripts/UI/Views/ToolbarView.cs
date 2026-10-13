using System;
using System.Collections.Generic;
using MagicBox.Core;
using MagicBox.UI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicBox.UI.Views
{
    public class ToolbarView
    {
        public event Action OnImport;
        public event Action OnScanFolder;
        public event Action<string> OnSearchChanged;
        public event Action<int> OnViewModeChanged;
        public event Action<string, bool> OnSortChanged;

        private VisualElement _root;
        private Button _gridBtn, _listBtn;
        private TextField _searchField;
        private string _sortField = "imported_at";
        private bool _sortDesc = true;

        public ToolbarView(VisualElement root)
        {
            _root = root;
        }

        public VisualElement Build()
        {
            var bar = new VisualElement();
            bar.AddToClassList("mb-toolbar");

            var importBtn = new Button(() => OnImport?.Invoke()) { text = Loc.T("toolbar.import") };
            importBtn.AddToClassList("mb-btn"); importBtn.AddToClassList("mb-btn-primary");
            bar.Add(importBtn);

            var scanBtn = new Button(() => OnScanFolder?.Invoke()) { text = Loc.T("toolbar.scan_folder") };
            scanBtn.AddToClassList("mb-btn");
            bar.Add(scanBtn);

            var spacer = new VisualElement(); spacer.AddToClassList("mb-spacer"); bar.Add(spacer);

            _searchField = new TextField(); _searchField.AddToClassList("mb-search");
            _searchField.RegisterValueChangedCallback(e => OnSearchChanged?.Invoke(e.newValue));
            bar.Add(_searchField);

            Button sortBtn = null;
            sortBtn = new Button(() => ShowSortMenu(sortBtn)) { text = Loc.T("toolbar.sort") };
            sortBtn.AddToClassList("mb-btn");
            bar.Add(sortBtn);

            _gridBtn = new Button(() => OnViewModeChanged?.Invoke(0)) { text = Loc.T("toolbar.grid") };
            _gridBtn.AddToClassList("mb-btn"); bar.Add(_gridBtn);

            _listBtn = new Button(() => OnViewModeChanged?.Invoke(1)) { text = Loc.T("toolbar.list") };
            _listBtn.AddToClassList("mb-btn"); bar.Add(_listBtn);

            var langBtn = new Button(() =>
            {
                Loc.SetLanguage(Loc.Current == Lang.En ? Lang.Zh : Lang.En);
            }) { text = Loc.T("toolbar.lang") };
            langBtn.AddToClassList("mb-btn");
            langBtn.style.minWidth = 32;
            bar.Add(langBtn);

            return bar;
        }

        public void FocusSearch()
        {
            _searchField?.Focus();
        }

        public void SetActiveMode(int mode)
        {
            _gridBtn.style.backgroundColor = mode == 0 ? new Color(0f, 0.47f, 0.83f, 0.4f) : Color.clear;
            _listBtn.style.backgroundColor = mode == 1 ? new Color(0f, 0.47f, 0.83f, 0.4f) : Color.clear;
        }

        private void ShowSortMenu(VisualElement anchor)
        {
            var ctx = new ContextMenuPopup();
            var pos = new Vector2(anchor.worldBound.x, anchor.worldBound.yMax);
            var items = new List<ContextMenuPopup.MenuItem>
            {
                new ContextMenuPopup.MenuItem(Loc.T("toolbar.sort.date_newest"), () => DoSort("imported_at", true)),
                new ContextMenuPopup.MenuItem(Loc.T("toolbar.sort.date_oldest"), () => DoSort("imported_at", false)),
                ContextMenuPopup.MenuItem.Separator(),
                new ContextMenuPopup.MenuItem(Loc.T("toolbar.sort.name_az"), () => DoSort("name", false)),
                new ContextMenuPopup.MenuItem(Loc.T("toolbar.sort.name_za"), () => DoSort("name", true)),
                ContextMenuPopup.MenuItem.Separator(),
                new ContextMenuPopup.MenuItem(Loc.T("toolbar.sort.size_largest"), () => DoSort("file_size", true)),
                new ContextMenuPopup.MenuItem(Loc.T("toolbar.sort.size_smallest"), () => DoSort("file_size", false)),
                ContextMenuPopup.MenuItem.Separator(),
                new ContextMenuPopup.MenuItem(Loc.T("toolbar.sort.type"), () => DoSort("resource_type", false)),
                new ContextMenuPopup.MenuItem(Loc.T("toolbar.sort.rating"), () => DoSort("rating", true)),
            };
            ctx.Show(_root, pos, items);
        }

        private void DoSort(string field, bool desc)
        {
            _sortField = field; _sortDesc = desc;
            OnSortChanged?.Invoke(field, desc);
        }
    }
}
