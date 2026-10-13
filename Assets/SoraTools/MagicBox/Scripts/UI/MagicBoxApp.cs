using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicBox.Core;
using MagicBox.Models;
using MagicBox.Services;
using MagicBox.UI.Components;
using MagicBox.UI.Views;
using SoraTools.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicBox.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class MagicBoxApp : MonoBehaviour
    {
        [SerializeField] private StyleSheet variablesStyle;
        [SerializeField] private StyleSheet mainStyle;

        private UIDocument _uiDoc;
        private VisualElement _root;

        // Services & ViewModel
        private ServiceContainer _services;
        private ContentViewModel _vm;

        // Views
        private WelcomeView _welcomeView;
        private ToolbarView _toolbarView;
        private SidebarView _sidebarView;
        private ContentGridView _gridView;
        private ContentListView _listView;
        private DetailPanelView _detailView;

        // UI State
        private VisualElement _welcomeContainer;
        private VisualElement _mainContainer;
        private Label _contentTitle;
        private Label _contentCount;
        private ContextMenuPopup _contextMenu = new ContextMenuPopup();
        private InputDialogPopup _inputDialog = new InputDialogPopup();
        private int _viewMode;
        private string _baseTitle;

        // Progress bar UI
        private VisualElement _progressBar;
        private VisualElement _progressFill;
        private Label _progressLabel;
        private Button _progressCancelBtn;
        private volatile bool _importPendingRefresh;

        // Progress bar auto-hide timer
        private IVisualElementScheduledItem _progressHideSchedule;

        // Drag-to-folder state
        private VisualElement _contentBody;
        private bool _isDragging;
        private bool _dragPending;
        private Vector2 _dragStartPos;
        private List<ResourceItem> _dragItems;
        private VisualElement _dragGhost;

        // Import results for thumbnail generation
        private List<ResourceItem> _importedItems;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.SetResolution(1280, 800, false);
        }

        private void Start()
        {
            _uiDoc = GetComponent<UIDocument>();
            _root = _uiDoc.rootVisualElement;

            InitServices();
            BuildUI();
            _services.Library.TryAutoOpenLastLibrary();

            #if !UNITY_EDITOR
            Win32DragDrop.Enable();
            #endif
        }

        private void Update()
        {
            // Poll async import progress
            if (_services.Import.IsImporting)
            {
                var p = _services.Import.CurrentProgress;
                if (p != null)
                {
                    float pct = p.TotalFiles > 0 ? (float)p.CurrentIndex / p.TotalFiles : 0;
                    _progressFill.style.width = Length.Percent(pct * 100);
                    _progressLabel.text = Loc.T("progress.importing", p.CurrentIndex, p.TotalFiles, p.CurrentFileName);
                    ShowProgressBar(true);
                }
            }
            else if (_importPendingRefresh)
            {
                _importPendingRefresh = false;
                var p = _services.Import.CurrentProgress;
                if (p != null)
                    _progressLabel.text = Loc.T("progress.done", p.ImportedCount, p.SkippedCount, p.FailedCount);
                ShowProgressBar(false);

                // Generate thumbnails for newly imported images (main thread)
                if (_importedItems != null && _importedItems.Count > 0)
                {
                    _services.Import.GenerateThumbnails(_importedItems);
                    _importedItems = null;
                }

                Refresh();
            }

            #if !UNITY_EDITOR
            var dropped = Win32DragDrop.GetDroppedFiles();
            if (dropped != null && dropped.Length > 0 && _services.Library.IsLibraryOpen)
            {
                var dropPoint = Win32DragDrop.GetDropPoint();
                if (_detailView.TryHandleDrop(dropped, dropPoint))
                {
                    // Cover change already handled via OnResourceUpdated → OnDetailResourceUpdated
                }
                else
                {
                    StartAsyncImport(dropped);
                }
            }
            #endif
        }

        private void OnDestroy()
        {
            _services.Library.OnLibraryChanged -= OnLibraryChanged;
            Loc.OnLanguageChanged -= OnLanguageChanged;

            #if !UNITY_EDITOR
            Win32DragDrop.Disable();
            #endif
        }

        private void OnLanguageChanged()
        {
            bool wasOpen = _services.Library.IsLibraryOpen;
            BuildUI();
            if (wasOpen)
            {
                _welcomeContainer.style.display = DisplayStyle.None;
                _mainContainer.style.display = DisplayStyle.Flex;
                Refresh();
            }
        }

        private void InitServices()
        {
            _services = new ServiceContainer();
            _vm = new ContentViewModel(_services);
            _services.Library.OnLibraryChanged += OnLibraryChanged;
            _viewMode = AppSettings.Instance.ViewMode;
            Loc.Init();
            Loc.OnLanguageChanged += OnLanguageChanged;
        }

        private void BuildUI()
        {
            _root.Clear();
            // Only add stylesheets if not already present (Clear() doesn't remove them)
            if (variablesStyle != null && !_root.styleSheets.Contains(variablesStyle))
                _root.styleSheets.Add(variablesStyle);
            if (mainStyle != null && !_root.styleSheets.Contains(mainStyle))
                _root.styleSheets.Add(mainStyle);

            var container = new VisualElement();
            container.AddToClassList("mb-root");
            container.style.flexGrow = 1;
            _root.Add(container);

            // Welcome
            _welcomeView = new WelcomeView();
            _welcomeView.OnCreateLibrary += OnCreateLibrary;
            _welcomeView.OnOpenLibrary += OnOpenLibrary;
            _welcomeView.OnOpenRecent += path => _services.Library.OpenLibrary(path);
            _welcomeContainer = _welcomeView.Build(AppSettings.Instance.RecentLibraries);
            container.Add(_welcomeContainer);

            // Main
            _mainContainer = new VisualElement();
            _mainContainer.AddToClassList("mb-root");
            _mainContainer.style.display = DisplayStyle.None;
            container.Add(_mainContainer);

            // Toolbar
            _toolbarView = new ToolbarView(_root);
            _toolbarView.OnImport += OnImportFiles;
            _toolbarView.OnScanFolder += OnScanFolder;
            _toolbarView.OnSearchChanged += OnSearchChanged;
            _toolbarView.OnViewModeChanged += OnViewModeChanged;
            _toolbarView.OnSortChanged += OnSortChanged;
            _mainContainer.Add(_toolbarView.Build());

            // Progress bar (hidden by default)
            BuildProgressBar();
            _mainContainer.Add(_progressBar);

            // Main body
            var body = new VisualElement();
            body.AddToClassList("mb-main");
            _mainContainer.Add(body);

            // Sidebar
            _sidebarView = new SidebarView(_services.Folders, _services.Tags, _root, _inputDialog);
            _sidebarView.OnFolderSelected += OnFolderSelected;
            _sidebarView.OnAllSelected += OnAllSelected;
            _sidebarView.OnTagSelected += OnTagSelected;
            body.Add(_sidebarView.Build());

            // Content area
            var content = new VisualElement();
            content.AddToClassList("mb-content");
            body.Add(content);

            var header = new VisualElement();
            header.AddToClassList("mb-content-header");
            content.Add(header);
            _contentTitle = new Label(Loc.T("content.all"));
            _contentTitle.AddToClassList("mb-content-title");
            header.Add(_contentTitle);
            _contentCount = new Label(Loc.T("content.items", 0));
            _contentCount.AddToClassList("mb-content-count");
            header.Add(_contentCount);

            _contentBody = new VisualElement();
            _contentBody.AddToClassList("mb-content-body");
            content.Add(_contentBody);

            // Grid
            _gridView = new ContentGridView();
            _gridView.OnSelected += OnResourceSelected;
            _gridView.OnItemClicked += OnItemClicked;
            _gridView.OnDoubleClicked += OnResourceDoubleClick;
            _gridView.OnRightClicked += OnResourceRightClick;
            _contentBody.Add(_gridView.Build());

            // List
            _listView = new ContentListView();
            _listView.OnSelected += OnResourceSelected;
            _listView.OnItemClicked += OnItemClicked;
            _listView.OnDoubleClicked += OnResourceDoubleClick;
            _listView.OnRightClicked += OnResourceRightClick;
            _contentBody.Add(_listView.Build());

            // Detail
            _detailView = new DetailPanelView(_services.Tags, _services.Import, _services.Library, _root, _inputDialog);
            _detailView.OnResourceUpdated += OnDetailResourceUpdated;
            _detailView.OnCoverChanged += OnCoverThumbnailChanged;
            _contentBody.Add(_detailView.Build());

            UpdateViewMode();

            // Keyboard shortcuts
            container.focusable = true;
            container.RegisterCallback<KeyDownEvent>(OnKeyDown);

            // Drag-to-folder: detect drag from grid/list only (not detail panel)
            _gridView.Root.RegisterCallback<PointerDownEvent>(OnContentPointerDown);
            _listView.Root.RegisterCallback<PointerDownEvent>(OnContentPointerDown);
            // Unregister first to prevent handler accumulation on language change
            // (UnregisterCallback is safe to call even if not previously registered)
            _root.UnregisterCallback<PointerMoveEvent>(OnGlobalPointerMove, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerUpEvent>(OnGlobalPointerUp, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerMoveEvent>(OnGlobalPointerMove, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerUpEvent>(OnGlobalPointerUp, TrickleDown.TrickleDown);
        }

        // ============ Progress Bar ============

        private void BuildProgressBar()
        {
            _progressBar = new VisualElement();
            _progressBar.style.flexDirection = FlexDirection.Row;
            _progressBar.style.alignItems = Align.Center;
            _progressBar.style.height = 28;
            _progressBar.style.paddingLeft = 8;
            _progressBar.style.paddingRight = 8;
            _progressBar.style.backgroundColor = new Color(0.1f, 0.14f, 0.22f);
            _progressBar.style.display = DisplayStyle.None;

            _progressLabel = new Label("");
            _progressLabel.style.color = new Color(0.75f, 0.82f, 0.9f);
            _progressLabel.style.fontSize = 11;
            _progressLabel.style.flexGrow = 1;
            _progressBar.Add(_progressLabel);

            var trackOuter = new VisualElement();
            trackOuter.style.width = 200;
            trackOuter.style.height = 6;
            trackOuter.style.backgroundColor = new Color(0.2f, 0.25f, 0.35f);
            trackOuter.style.borderTopLeftRadius = 3;
            trackOuter.style.borderTopRightRadius = 3;
            trackOuter.style.borderBottomLeftRadius = 3;
            trackOuter.style.borderBottomRightRadius = 3;
            trackOuter.style.marginLeft = 8;
            _progressBar.Add(trackOuter);

            _progressFill = new VisualElement();
            _progressFill.style.width = Length.Percent(0);
            _progressFill.style.height = Length.Percent(100);
            _progressFill.style.backgroundColor = new Color(0f, 0.47f, 0.83f);
            _progressFill.style.borderTopLeftRadius = 3;
            _progressFill.style.borderTopRightRadius = 3;
            _progressFill.style.borderBottomLeftRadius = 3;
            _progressFill.style.borderBottomRightRadius = 3;
            trackOuter.Add(_progressFill);

            _progressCancelBtn = new Button(() => _services.Import.CancelImport()) { text = "\u2715" };
            _progressCancelBtn.style.marginLeft = 8;
            _progressCancelBtn.style.width = 22;
            _progressCancelBtn.style.height = 22;
            _progressCancelBtn.style.fontSize = 11;
            _progressBar.Add(_progressCancelBtn);
        }

        private void ShowProgressBar(bool importing)
        {
            _progressHideSchedule?.Pause();
            _progressBar.style.display = DisplayStyle.Flex;
            _progressCancelBtn.style.display = importing ? DisplayStyle.Flex : DisplayStyle.None;
            if (!importing)
            {
                _progressFill.style.width = Length.Percent(100);
                _progressHideSchedule = _root.schedule.Execute(() =>
                {
                    _progressBar.style.display = DisplayStyle.None;
                }).StartingIn(3000);
            }
        }

        // ============ Async Import ============

        private void StartAsyncImport(string[] paths)
        {
            _services.Import.ImportFilesAsync(paths, _vm.CurrentFolderId, items =>
            {
                _importedItems = items;
                _importPendingRefresh = true;
            });
        }

        private void StartAsyncScan(string folderPath)
        {
            _services.Import.ScanAndImportFolderAsync(folderPath, _vm.CurrentFolderId, items =>
            {
                _importedItems = items;
                _importPendingRefresh = true;
            });
        }

        // ============ Selection (delegates to ViewModel) ============

        private void OnItemClicked(ResourceItem item, int index, bool ctrl, bool shift)
        {
            _vm.HandleItemClick(item, index, ctrl, shift);
            _detailView.ShowItem(_vm.SelectedItem);
            UpdateSelectionHighlight();
        }

        private void UpdateSelectionHighlight()
        {
            if (_viewMode == 0)
                _gridView.SetSelectedIds(_vm.SelectedIds);
            else
                _listView.SetSelectedIds(_vm.SelectedIds);
        }

        // ============ Keyboard ============

        private void OnKeyDown(KeyDownEvent e)
        {
            if (!_services.Library.IsLibraryOpen) return;

            // Ctrl+Z - Undo
            if (e.keyCode == KeyCode.Z && e.ctrlKey)
            {
                _vm.PerformUndo();
                _detailView.ShowItem(_vm.SelectedItem);
                UpdateSelectionHighlight();
                UpdateContent();
                _sidebarView.Refresh();
                e.StopPropagation();
                return;
            }

            // Ctrl+A - Select all
            if (e.keyCode == KeyCode.A && e.ctrlKey)
            {
                _vm.SelectAll();
                UpdateSelectionHighlight();
                e.StopPropagation();
                return;
            }

            // Ctrl+F - Focus search
            if (e.keyCode == KeyCode.F && e.ctrlKey)
            {
                _toolbarView.FocusSearch();
                e.StopPropagation();
                return;
            }

            // F5 - Refresh
            if (e.keyCode == KeyCode.F5)
            {
                Refresh();
                e.StopPropagation();
                return;
            }

            // Delete
            if (e.keyCode == KeyCode.Delete)
            {
                var items = _vm.GetSelectedItems();
                if (items.Count == 0) return;

                string msg = items.Count == 1
                    ? Loc.T("ctx.delete_msg", items[0].Name)
                    : Loc.T("batch.delete_msg", items.Count);

                _inputDialog.Show(_root, Loc.T("ctx.delete_title"), msg, () =>
                {
                    _vm.DeleteWithUndo(items);
                    _detailView.ShowItem(null);
                    UpdateSelectionHighlight();
                    UpdateContent();
                    _sidebarView.Refresh();
                });
                e.StopPropagation();
                return;
            }

            // Escape - Clear selection
            if (e.keyCode == KeyCode.Escape)
            {
                _vm.ClearSelection();
                _detailView.ShowItem(null);
                UpdateSelectionHighlight();
                e.StopPropagation();
                return;
            }

            // Arrow keys - Navigate selection
            if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow ||
                e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow)
            {
                NavigateSelection(e.keyCode, e.shiftKey);
                e.StopPropagation();
                return;
            }

            // Enter - Open resource
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                if (_vm.SelectedItem != null) _services.Import.OpenResourceFile(_vm.SelectedItem);
                e.StopPropagation();
                return;
            }

            // Home - Select first item
            if (e.keyCode == KeyCode.Home)
            {
                if (_vm.CurrentItems.Count > 0)
                {
                    _vm.SelectItemAtIndex(0, e.shiftKey);
                    _detailView.ShowItem(_vm.SelectedItem);
                    UpdateSelectionHighlight();
                }
                e.StopPropagation();
                return;
            }

            // End - Select last item
            if (e.keyCode == KeyCode.End)
            {
                if (_vm.CurrentItems.Count > 0)
                {
                    _vm.SelectItemAtIndex(_vm.CurrentItems.Count - 1, e.shiftKey);
                    _detailView.ShowItem(_vm.SelectedItem);
                    UpdateSelectionHighlight();
                }
                e.StopPropagation();
            }
        }

        // ============ Events ============

        private void OnLibraryChanged()
        {
            bool open = _services.Library.IsLibraryOpen;
            _welcomeContainer.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;
            _mainContainer.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open) Refresh();
        }

        private void OnCreateLibrary()
        {
            string path = Win32Dialog.OpenFolderDialog(Loc.T("win32.choose_library"));
            if (string.IsNullOrEmpty(path)) return;
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = "My Library";
            _services.Library.CreateLibrary(name, path);
        }

        private void OnOpenLibrary()
        {
            string path = Win32Dialog.OpenFolderDialog(Loc.T("win32.open_library"));
            if (string.IsNullOrEmpty(path)) return;
            _services.Library.OpenLibrary(path);
        }

        private void OnImportFiles()
        {
            var paths = Win32Dialog.OpenFileDialog(Loc.T("win32.import_files"), Loc.T("win32.import_filter"));
            if (paths != null && paths.Length > 0) StartAsyncImport(paths);
        }

        private void OnScanFolder()
        {
            string path = Win32Dialog.OpenFolderDialog(Loc.T("win32.scan_folder"));
            if (!string.IsNullOrEmpty(path)) StartAsyncScan(path);
        }

        private void OnSearchChanged(string kw)
        {
            if (!string.IsNullOrEmpty(kw))
                _contentTitle.text = Loc.T("content.search", kw);
            else
                _contentTitle.text = _baseTitle ?? Loc.T("content.all");
            _vm.SearchByKeyword(kw);
            UpdateContent();
        }

        private void OnViewModeChanged(int mode)
        {
            _viewMode = mode;
            AppSettings.Instance.ViewMode = mode;
            AppSettings.Instance.Save();
            UpdateViewMode();
        }

        private void OnSortChanged(string field, bool desc)
        {
            _vm.SortBy(field, desc);
            UpdateContent();
        }

        private void OnFolderSelected(string id, string name)
        {
            _baseTitle = name;
            _contentTitle.text = name;
            _vm.ShowFolder(id);
            UpdateContent();
        }

        private void OnAllSelected()
        {
            _baseTitle = Loc.T("content.all");
            _contentTitle.text = _baseTitle;
            _vm.ShowAll();
            UpdateContent();
        }

        private void OnTagSelected(string id, string name)
        {
            _baseTitle = Loc.T("content.tag_prefix", name);
            _contentTitle.text = _baseTitle;
            _vm.ShowTag(id);
            UpdateContent();
        }

        /// <summary>
        /// Lightweight save: only writes to DB and updates the active view's card.
        /// Avoids full Refresh() which re-queries all resources from SQLite.
        /// </summary>
        private void OnDetailResourceUpdated(ResourceItem item)
        {
            _services.Library.Database.UpdateResourceMetadata(item);
            if (_viewMode == 0)
                _gridView.InvalidateCards();
            else
                _listView.InvalidateRows();
        }

        /// <summary>Evict the OLD thumbnail from grid cache when cover image is changed or cleared.</summary>
        private void OnCoverThumbnailChanged(string oldThumbPath)
        {
            if (_viewMode == 0)
                _gridView.EvictThumb(oldThumbPath);
        }

        private void OnResourceSelected(ResourceItem item)
        {
            _vm.SelectSingle(item);
            _detailView.ShowItem(item);
            UpdateSelectionHighlight();
        }

        private void OnResourceDoubleClick(ResourceItem item) => _services.Import.OpenResourceFile(item);

        private void OnResourceRightClick(ResourceItem item, Vector2 pos)
        {
            var selected = _vm.GetSelectedItems();
            bool multi = selected.Count > 1;

            var items = new List<ContextMenuPopup.MenuItem>();

            if (!multi)
            {
                items.Add(new ContextMenuPopup.MenuItem(Loc.T("ctx.open_file"), () => _services.Import.OpenResourceFile(item)));
                items.Add(new ContextMenuPopup.MenuItem(Loc.T("ctx.open_folder"), () => _services.Import.OpenContainingFolder(item)));
                items.Add(ContextMenuPopup.MenuItem.Separator());
                items.Add(new ContextMenuPopup.MenuItem(Loc.T("ctx.copy_to"), () =>
                {
                    string dest = Win32Dialog.OpenFolderDialog(Loc.T("win32.copy_to"));
                    if (!string.IsNullOrEmpty(dest)) _services.Import.CopyResourceTo(item, dest);
                }));
                #if UNITY_EDITOR
                if (item.Type == ResourceType.UnityPackage)
                    items.Add(new ContextMenuPopup.MenuItem(Loc.T("ctx.import_unity"), () => _services.Import.ImportToUnity(item)));
                #endif
                items.Add(ContextMenuPopup.MenuItem.Separator());
            }

            // Batch tag add
            if (selected.Count >= 1)
            {
                items.Add(new ContextMenuPopup.MenuItem(Loc.T("batch.add_tag"), () => ShowBatchTagMenu(selected, pos)));
                items.Add(new ContextMenuPopup.MenuItem(Loc.T("batch.move_folder"), () => ShowBatchMoveMenu(selected, pos)));
                items.Add(ContextMenuPopup.MenuItem.Separator());
            }

            string deleteLabel = multi ? Loc.T("batch.delete", selected.Count) : Loc.T("ctx.delete");
            items.Add(new ContextMenuPopup.MenuItem(deleteLabel, () =>
            {
                string msg = multi
                    ? Loc.T("batch.delete_msg", selected.Count)
                    : Loc.T("ctx.delete_msg", item.Name);
                _inputDialog.Show(_root, Loc.T("ctx.delete_title"), msg, () =>
                {
                    _vm.DeleteWithUndo(selected);
                    _detailView.ShowItem(null);
                    UpdateSelectionHighlight();
                    UpdateContent();
                    _sidebarView.Refresh();
                });
            }));

            _contextMenu.Show(_root, pos, items);
        }

        // ============ Batch Operations ============

        private void ShowBatchTagMenu(List<ResourceItem> items, Vector2 pos)
        {
            var allTags = _services.Tags.GetAllTags();
            var rids = items.Select(r => r.Id).ToList();
            var menuItems = allTags.Select(t => new ContextMenuPopup.MenuItem(t.Name, () =>
            {
                _services.Tags.AddTagToResources(rids, t.Id);
                Refresh();
            })).ToList();
            var ctx = new ContextMenuPopup();
            ctx.Show(_root, pos, menuItems);
        }

        private void ShowBatchMoveMenu(List<ResourceItem> items, Vector2 pos)
        {
            var allFolders = _services.Folders.GetAllFolders();
            var rids = items.Select(r => r.Id).ToList();
            var menuItems = new List<ContextMenuPopup.MenuItem>();
            menuItems.Add(new ContextMenuPopup.MenuItem(Loc.T("sidebar.uncategorized"), () =>
            {
                _services.Folders.MoveResourcesToFolder(rids, null);
                Refresh();
            }));
            foreach (var f in allFolders)
            {
                var folder = f;
                menuItems.Add(new ContextMenuPopup.MenuItem(folder.Name, () =>
                {
                    _services.Folders.MoveResourcesToFolder(rids, folder.Id);
                    Refresh();
                }));
            }
            var ctx = new ContextMenuPopup();
            ctx.Show(_root, pos, menuItems);
        }

        // ============ Keyboard Navigation ============

        private void NavigateSelection(KeyCode key, bool shift)
        {
            if (_vm.CurrentItems.Count == 0) return;

            int delta = 0;
            if (_viewMode == 0) // Grid
            {
                int cols = _gridView.ColumnsPerRow;
                switch (key)
                {
                    case KeyCode.LeftArrow: delta = -1; break;
                    case KeyCode.RightArrow: delta = 1; break;
                    case KeyCode.UpArrow: delta = -cols; break;
                    case KeyCode.DownArrow: delta = cols; break;
                }
            }
            else // List
            {
                switch (key)
                {
                    case KeyCode.UpArrow: delta = -1; break;
                    case KeyCode.DownArrow: delta = 1; break;
                }
            }

            if (delta != 0)
            {
                _vm.Navigate(delta, shift);
                _detailView.ShowItem(_vm.SelectedItem);
                UpdateSelectionHighlight();
            }
        }

        // ============ Drag to Folder ============

        private void OnContentPointerDown(PointerDownEvent e)
        {
            if (e.button == 0 && _vm.SelectedIds.Count > 0)
            {
                _dragStartPos = e.position;
                _dragPending = true;
            }
        }

        private void OnGlobalPointerMove(PointerMoveEvent e)
        {
            if (!_dragPending && !_isDragging) return;

            if (_dragPending)
            {
                if ((e.pressedButtons & 1) == 0) { _dragPending = false; return; }
                if (Vector2.Distance(e.position, _dragStartPos) < 10f) return;

                _dragPending = false;
                var items = _vm.GetSelectedItems();
                if (items.Count == 0) return;
                StartDrag(items, e.position);
                e.StopPropagation();
                return;
            }

            if (_isDragging)
            {
                _dragGhost.style.left = e.position.x + 12;
                _dragGhost.style.top = e.position.y + 12;

                string folderId = _sidebarView.GetFolderIdAtWorldPosition(e.position);
                _sidebarView.SetDropHighlight(folderId);
                e.StopPropagation();
            }
        }

        private void OnGlobalPointerUp(PointerUpEvent e)
        {
            _dragPending = false;
            if (_isDragging) EndDrag(e.position);
        }

        private void StartDrag(List<ResourceItem> items, Vector2 pos)
        {
            _isDragging = true;
            _dragItems = items;

            _dragGhost = new VisualElement();
            _dragGhost.pickingMode = PickingMode.Ignore;
            _dragGhost.style.position = Position.Absolute;
            _dragGhost.style.backgroundColor = new Color(0f, 0.47f, 0.83f, 0.85f);
            _dragGhost.style.paddingLeft = 8;
            _dragGhost.style.paddingRight = 8;
            _dragGhost.style.paddingTop = 4;
            _dragGhost.style.paddingBottom = 4;
            _dragGhost.style.borderTopLeftRadius = 4;
            _dragGhost.style.borderTopRightRadius = 4;
            _dragGhost.style.borderBottomLeftRadius = 4;
            _dragGhost.style.borderBottomRightRadius = 4;

            string ghostText = items.Count == 1 ? items[0].Name : Loc.T("drag.items", items.Count);
            var label = new Label(ghostText);
            label.pickingMode = PickingMode.Ignore;
            label.style.color = Color.white;
            label.style.fontSize = 11;
            _dragGhost.Add(label);

            _dragGhost.style.left = pos.x + 12;
            _dragGhost.style.top = pos.y + 12;
            _root.Add(_dragGhost);
        }

        private void EndDrag(Vector2 pos)
        {
            if (!_isDragging) return;
            _isDragging = false;

            string folderId = _sidebarView.GetFolderIdAtWorldPosition(pos);
            _sidebarView.SetDropHighlight(null);

            if (_dragGhost != null && _dragGhost.parent != null)
                _dragGhost.parent.Remove(_dragGhost);
            _dragGhost = null;

            if (folderId != null && _dragItems != null && _dragItems.Count > 0)
            {
                var rids = new List<string>();
                foreach (var it in _dragItems) rids.Add(it.Id);
                _services.Folders.MoveResourcesToFolder(rids, folderId);
                Refresh();
            }

            _dragItems = null;
        }

        // ============ View ============

        private void Refresh()
        {
            if (!_services.Library.IsLibraryOpen) return;
            _vm.LoadResources();
            UpdateContent();
            _sidebarView.Refresh();
        }

        private void UpdateContent()
        {
            _contentCount.text = Loc.T("content.items", _vm.CurrentItems.Count);
            if (_viewMode == 0)
                _gridView.SetItems(_vm.CurrentItems);
            else
                _listView.SetItems(_vm.CurrentItems);
        }

        private void UpdateViewMode()
        {
            _gridView.Root.style.display = _viewMode == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _listView.Root.style.display = _viewMode == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            _toolbarView.SetActiveMode(_viewMode);
            // Push items and selection into the newly visible view
            if (_vm != null && _vm.CurrentItems != null)
            {
                if (_viewMode == 0)
                {
                    _gridView.SetItems(_vm.CurrentItems);
                    _gridView.SetSelectedIds(_vm.SelectedIds);
                }
                else
                {
                    _listView.SetItems(_vm.CurrentItems);
                    _listView.SetSelectedIds(_vm.SelectedIds);
                }
            }
        }
    }
}
