using System;
using System.Collections.Generic;
using MagicBox.Core;
using MagicBox.Models;
using MagicBox.Services;
using MagicBox.UI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicBox.UI.Views
{
    public class SidebarView
    {
        public event Action<string, string> OnFolderSelected;
        public event Action OnAllSelected;
        public event Action<string, string> OnTagSelected;

        private readonly FolderService _folders;
        private readonly TagService _tags;
        private readonly VisualElement _dialogRoot;
        private readonly InputDialogPopup _dialog;

        private VisualElement _folderContainer;
        private VisualElement _tagContainer;
        private string _selectedId;
        private Dictionary<string, VisualElement> _folderElements = new Dictionary<string, VisualElement>();

        private static readonly string[] Colors = { "#4FC3F7", "#66BB6A", "#FFA726", "#EF5350", "#AB47BC", "#26C6DA", "#FFEE58", "#EC407A" };

        public SidebarView(FolderService folders, TagService tags, VisualElement dialogRoot, InputDialogPopup dialog)
        {
            _folders = folders; _tags = tags; _dialogRoot = dialogRoot; _dialog = dialog;
        }

        public VisualElement Build()
        {
            var sidebar = new VisualElement();
            sidebar.AddToClassList("mb-sidebar");

            // Folders section
            var fHeader = new VisualElement(); fHeader.AddToClassList("mb-section-header"); sidebar.Add(fHeader);
            var fTitle = new Label(Loc.T("sidebar.folders")); fTitle.AddToClassList("mb-section-title"); fHeader.Add(fTitle);
            var fAdd = new Button(() => CreateFolder()) { text = "+" }; fAdd.AddToClassList("mb-add-btn"); fHeader.Add(fAdd);

            // All resources
            sidebar.Add(MakeTreeItem(null, Loc.T("sidebar.all"), null, true));
            sidebar.Add(MakeTreeItem("__uncat__", Loc.T("sidebar.uncategorized"), null, false));

            _folderContainer = new VisualElement();
            sidebar.Add(_folderContainer);

            // Tags section
            var tHeader = new VisualElement(); tHeader.AddToClassList("mb-section-header"); tHeader.style.marginTop = 12; sidebar.Add(tHeader);
            var tTitle = new Label(Loc.T("sidebar.tags")); tTitle.AddToClassList("mb-section-title"); tHeader.Add(tTitle);
            var tAdd = new Button(() => CreateTag()) { text = "+" }; tAdd.AddToClassList("mb-add-btn"); tHeader.Add(tAdd);

            _tagContainer = new ScrollView(ScrollViewMode.Vertical);
            _tagContainer.style.flexGrow = 1;
            _tagContainer.style.maxHeight = 250;
            _tagContainer.style.borderTopWidth = 1;
            _tagContainer.style.borderTopColor = new Color(0.16f, 0.21f, 0.31f);
            _tagContainer.style.paddingTop = 4;
            sidebar.Add(_tagContainer);

            return sidebar;
        }

        public void Refresh()
        {
            RefreshFolders();
            RefreshTags();
        }

        private void RefreshFolders()
        {
            _folderContainer.Clear();
            _folderElements.Clear();
            foreach (var f in _folders.GetRootFolders())
                BuildFolderNode(f, _folderContainer, 0);
        }

        private void BuildFolderNode(ResourceFolder f, VisualElement parent, int depth)
        {
            var el = MakeTreeItem(f.Id, f.Name, f.ColorTag, false, depth);
            el.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) { ShowFolderMenu(f, e.position); e.StopPropagation(); e.PreventDefault(); } });
            parent.Add(el);
            _folderElements[f.Id] = el;
            foreach (var child in _folders.GetChildFolders(f.Id))
                BuildFolderNode(child, parent, depth + 1);
        }

        private void RefreshTags()
        {
            _tagContainer.Clear();
            foreach (var g in _tags.GetTagsByGroup())
            {
                foreach (var t in g.Value)
                {
                    var item = new VisualElement(); item.AddToClassList("mb-tree-item");
                    item.RegisterCallback<ClickEvent>(e => { _selectedId = "tag_" + t.Id; OnTagSelected?.Invoke(t.Id, t.Name); UpdateSel(); });
                    item.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) { ShowTagMenu(t, e.position); e.StopPropagation(); e.PreventDefault(); } });
                    item.userData = "tag_" + t.Id;

                    var dot = new VisualElement();
                    dot.style.width = 8; dot.style.height = 8;
                    dot.style.borderTopLeftRadius = 4; dot.style.borderTopRightRadius = 4;
                    dot.style.borderBottomLeftRadius = 4; dot.style.borderBottomRightRadius = 4;
                    dot.style.marginRight = 6;
                    if (ColorUtility.TryParseHtmlString(t.Color, out Color c)) dot.style.backgroundColor = c;
                    item.Add(dot);

                    var lbl = new Label(t.Name); lbl.AddToClassList("mb-tree-label"); item.Add(lbl);
                    var cnt = new Label(t.UsageCount.ToString()); cnt.AddToClassList("mb-tree-count"); item.Add(cnt);
                    _tagContainer.Add(item);
                }
            }
        }

        private VisualElement MakeTreeItem(string id, string name, string color, bool isAll, int depth = 0)
        {
            var item = new VisualElement(); item.AddToClassList("mb-tree-item");
            item.style.paddingLeft = 10 + depth * 18;
            item.userData = id;
            item.RegisterCallback<ClickEvent>(e =>
            {
                _selectedId = id;
                if (isAll || id == null) OnAllSelected?.Invoke();
                else if (id == "__uncat__") OnFolderSelected?.Invoke("", Loc.T("sidebar.uncategorized"));
                else OnFolderSelected?.Invoke(id, name);
                UpdateSel();
            });
            var icon = new Label(isAll ? "\u25A3" : "\u25A0"); icon.AddToClassList("mb-tree-icon");
            if (!string.IsNullOrEmpty(color) && ColorUtility.TryParseHtmlString(color, out Color cc)) icon.style.color = cc;
            else icon.style.color = new Color(0.48f, 0.55f, 0.63f);
            item.Add(icon);
            var lbl = new Label(name); lbl.AddToClassList("mb-tree-label"); item.Add(lbl);
            return item;
        }

        public string GetFolderIdAtWorldPosition(Vector2 worldPos)
        {
            foreach (var kvp in _folderElements)
            {
                if (kvp.Value.worldBound.Contains(worldPos))
                    return kvp.Key;
            }
            return null;
        }

        public void SetDropHighlight(string folderId)
        {
            foreach (var kvp in _folderElements)
                kvp.Value.EnableInClassList("mb-tree-item--drop-target", folderId != null && kvp.Key == folderId);
        }

        private void UpdateSel()
        {
            _folderContainer.parent.Query<VisualElement>(className: "mb-tree-item").ForEach(el =>
                el.EnableInClassList("mb-tree-item--selected", el.userData as string == _selectedId));
            _tagContainer.Query<VisualElement>(className: "mb-tree-item").ForEach(el =>
                el.EnableInClassList("mb-tree-item--selected", el.userData as string == _selectedId));
        }

        private void CreateFolder()
        {
            _dialog.Show(_dialogRoot, Loc.T("sidebar.new_folder"), Loc.T("sidebar.new_folder_prompt"), Loc.T("sidebar.new_folder_default"), name =>
            {
                _folders.CreateFolder(name, _selectedId == "__uncat__" ? null : _selectedId);
                RefreshFolders();
            });
        }

        private void CreateTag()
        {
            _dialog.Show(_dialogRoot, Loc.T("sidebar.new_tag"), Loc.T("sidebar.new_tag_prompt"), "", name => { _tags.CreateTag(name); RefreshTags(); });
        }

        private void ShowFolderMenu(ResourceFolder f, Vector2 mousePos)
        {
            var ctx = new ContextMenuPopup();
            var items = new List<ContextMenuPopup.MenuItem>
            {
                new ContextMenuPopup.MenuItem(Loc.T("sidebar.create_subfolder"), () =>
                    _dialog.Show(_dialogRoot, Loc.T("sidebar.new_subfolder_title"), Loc.T("sidebar.subfolder_prompt"), Loc.T("sidebar.subfolder_default"), n => { _folders.CreateFolder(n, f.Id); RefreshFolders(); })),
                new ContextMenuPopup.MenuItem(Loc.T("sidebar.rename"), () =>
                    _dialog.Show(_dialogRoot, Loc.T("sidebar.rename_title"), Loc.T("sidebar.rename_prompt"), f.Name, n => { _folders.RenameFolder(f.Id, n); RefreshFolders(); })),
                ContextMenuPopup.MenuItem.Separator(),
            };
            foreach (var c in Colors)
            {
                string cc = c;
                items.Add(new ContextMenuPopup.MenuItem(Loc.T("sidebar.color_prefix", cc), () => { _folders.SetFolderColor(f.Id, cc); RefreshFolders(); }));
            }
            items.Add(ContextMenuPopup.MenuItem.Separator());
            items.Add(new ContextMenuPopup.MenuItem(Loc.T("sidebar.delete"), () =>
                _dialog.Show(_dialogRoot, Loc.T("sidebar.delete_folder_title"), Loc.T("sidebar.delete_folder_msg", f.Name), () => { _folders.DeleteFolder(f.Id); RefreshFolders(); OnAllSelected?.Invoke(); })));
            ctx.Show(_dialogRoot, mousePos, items);
        }

        private void ShowTagMenu(ResourceTag t, Vector2 mousePos)
        {
            var ctx = new ContextMenuPopup();
            var items = new List<ContextMenuPopup.MenuItem>
            {
                new ContextMenuPopup.MenuItem(Loc.T("sidebar.rename"), () =>
                    _dialog.Show(_dialogRoot, Loc.T("sidebar.rename_tag"), Loc.T("sidebar.rename_tag_prompt"), t.Name, n => { t.Name = n; _tags.UpdateTag(t); RefreshTags(); })),
                ContextMenuPopup.MenuItem.Separator(),
                new ContextMenuPopup.MenuItem(Loc.T("sidebar.delete"), () =>
                    _dialog.Show(_dialogRoot, Loc.T("sidebar.delete_tag_title"), Loc.T("sidebar.delete_tag_msg", t.Name), () => { _tags.DeleteTag(t.Id); RefreshTags(); })),
            };
            ctx.Show(_dialogRoot, mousePos, items);
        }
    }
}
