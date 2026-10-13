using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicBox.Core;
using MagicBox.Models;
using MagicBox.Services;
using MagicBox.UI.Components;
using SoraTools.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicBox.UI.Views
{
    public class DetailPanelView
    {
        public event Action<ResourceItem> OnResourceUpdated;
        /// <summary>Fires with the OLD thumbnail path when cover is changed/cleared, for cache eviction.</summary>
        public event Action<string> OnCoverChanged;

        private readonly TagService _tags;
        private readonly ImportService _import;
        private readonly LibraryManager _lib;
        private readonly VisualElement _dialogRoot;
        private readonly InputDialogPopup _dialog;

        private ResourceItem _current;
        private VisualElement _content;
        private VisualElement _empty;
        private VisualElement _iconContainer;
        private Label _nameLabel, _typeLabel;
        private Label _sizeVal, _dateVal, _pathVal;
        private VisualElement _starRow, _tagsContainer;
        private Button _addTagBtn;
        private TextField _versionField, _urlField, _descField;
        private VisualElement _coverPreview;
        private Texture2D _coverTexture;
        private IVisualElementScheduledItem _saveSchedule;
        private const long SaveDelayMs = 500;
        #if UNITY_EDITOR
        private Button _importUnityBtn;
        #endif

        public DetailPanelView(TagService tags, ImportService import, LibraryManager lib, VisualElement dialogRoot, InputDialogPopup dialog)
        {
            _tags = tags; _import = import; _lib = lib; _dialogRoot = dialogRoot; _dialog = dialog;
        }

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("mb-detail");

            _empty = new VisualElement();
            _empty.AddToClassList("mb-empty");
            _empty.style.flexGrow = 1;
            _empty.Add(new Label(Loc.T("detail.select")) { style = { fontSize = 13, color = new Color(0.48f, 0.55f, 0.63f) } });
            root.Add(_empty);

            _content = new ScrollView(ScrollViewMode.Vertical);
            _content.style.flexGrow = 1;
            _content.style.display = DisplayStyle.None;
            root.Add(_content);

            // Header
            var header = new VisualElement(); header.AddToClassList("mb-detail-header"); _content.Add(header);
            _iconContainer = new VisualElement();
            _iconContainer.style.marginBottom = 8;
            _iconContainer.style.alignItems = Align.Center;
            header.Add(_iconContainer);
            _nameLabel = new Label(); _nameLabel.AddToClassList("mb-detail-name"); header.Add(_nameLabel);
            _typeLabel = new Label(); _typeLabel.AddToClassList("mb-detail-type"); header.Add(_typeLabel);

            // Rating
            _starRow = new VisualElement(); _starRow.AddToClassList("mb-star-row"); header.Add(_starRow);
            for (int i = 1; i <= 5; i++)
            {
                int r = i;
                var star = new Button(() => SetRating(r)) { text = "\u2605" };
                star.AddToClassList("mb-star");
                _starRow.Add(star);
            }

            // Cover Image
            var coverSec = Section(Loc.T("detail.cover")); _content.Add(coverSec);
            _coverPreview = new VisualElement();
            _coverPreview.style.width = Length.Percent(100);
            _coverPreview.style.height = 120;
            _coverPreview.style.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
            _coverPreview.style.borderTopLeftRadius = _coverPreview.style.borderTopRightRadius =
                _coverPreview.style.borderBottomLeftRadius = _coverPreview.style.borderBottomRightRadius = 4;
            _coverPreview.style.borderTopWidth = _coverPreview.style.borderBottomWidth =
                _coverPreview.style.borderLeftWidth = _coverPreview.style.borderRightWidth = 1;
            _coverPreview.style.borderTopColor = _coverPreview.style.borderBottomColor =
                _coverPreview.style.borderLeftColor = _coverPreview.style.borderRightColor = new Color(0.35f, 0.40f, 0.50f);
            _coverPreview.style.alignItems = Align.Center;
            _coverPreview.style.justifyContent = Justify.Center;
            _coverPreview.style.overflow = Overflow.Hidden;
            _coverPreview.RegisterCallback<ClickEvent>(e => SelectCoverImage());
            coverSec.Add(_coverPreview);

            var coverBtns = new VisualElement();
            coverBtns.style.flexDirection = FlexDirection.Row;
            coverBtns.style.marginTop = 4;
            var selectCoverBtn = new Button(() => SelectCoverImage()) { text = Loc.T("detail.cover_select") };
            selectCoverBtn.AddToClassList("mb-btn"); selectCoverBtn.style.height = 22; selectCoverBtn.style.fontSize = 11; selectCoverBtn.style.flexGrow = 1;
            coverBtns.Add(selectCoverBtn);
            var clearCoverBtn = new Button(() => ClearCoverImage()) { text = Loc.T("detail.cover_clear") };
            clearCoverBtn.AddToClassList("mb-btn"); clearCoverBtn.style.height = 22; clearCoverBtn.style.fontSize = 11; clearCoverBtn.style.flexGrow = 1; clearCoverBtn.style.marginLeft = 4;
            coverBtns.Add(clearCoverBtn);
            coverSec.Add(coverBtns);

            // Info
            var info = Section(Loc.T("detail.info")); _content.Add(info);
            _sizeVal = Field(info, Loc.T("detail.size"));
            _dateVal = Field(info, Loc.T("detail.date"));
            _pathVal = Field(info, Loc.T("detail.path"));

            // Metadata
            var meta = Section(Loc.T("detail.metadata")); _content.Add(meta);
            _versionField = TextInput(meta, Loc.T("detail.version"), v => { if (_current != null) { _current.Version = v; ScheduleSave(); } });
            _urlField = TextInput(meta, Loc.T("detail.source_url"), v => { if (_current != null) { _current.SourceUrl = v; ScheduleSave(); } });

            // Tags
            var tagSec = Section(Loc.T("detail.tags")); _content.Add(tagSec);
            _tagsContainer = new VisualElement(); _tagsContainer.AddToClassList("mb-detail-tags"); tagSec.Add(_tagsContainer);
            _addTagBtn = new Button(() => ShowAddTagMenu()) { text = Loc.T("detail.add_tag") };
            _addTagBtn.AddToClassList("mb-btn"); _addTagBtn.style.height = 22; _addTagBtn.style.fontSize = 11; _addTagBtn.style.marginTop = 4;
            tagSec.Add(_addTagBtn);

            // Description
            var descSec = Section(Loc.T("detail.notes")); _content.Add(descSec);
            _descField = new TextField(); _descField.multiline = true; _descField.AddToClassList("mb-input-field");
            _descField.style.minHeight = 60;
            _descField.RegisterValueChangedCallback(e => { if (_current != null) { _current.Description = e.newValue; ScheduleSave(); } });
            descSec.Add(_descField);

            // Actions
            var actions = new VisualElement(); actions.AddToClassList("mb-detail-actions"); _content.Add(actions);

            #if UNITY_EDITOR
            _importUnityBtn = new Button(() => _import.ImportToUnity(_current)) { text = Loc.T("detail.import_unity") };
            _importUnityBtn.AddToClassList("mb-btn"); _importUnityBtn.AddToClassList("mb-btn-primary");
            _importUnityBtn.style.display = DisplayStyle.None;
            actions.Add(_importUnityBtn);
            #endif

            var openBtn = new Button(() => _import.OpenResourceFile(_current)) { text = Loc.T("detail.open_file") };
            openBtn.AddToClassList("mb-btn"); actions.Add(openBtn);

            var folderBtn = new Button(() => _import.OpenContainingFolder(_current)) { text = Loc.T("detail.open_folder") };
            folderBtn.AddToClassList("mb-btn"); actions.Add(folderBtn);

            var copyBtn = new Button(() =>
            {
                if (_current == null) return;
                string d = Win32Dialog.OpenFolderDialog(Loc.T("win32.copy_to"));
                if (!string.IsNullOrEmpty(d)) _import.CopyResourceTo(_current, d);
            }) { text = Loc.T("detail.copy_to") };
            copyBtn.AddToClassList("mb-btn"); actions.Add(copyBtn);

            return root;
        }

        public void ShowItem(ResourceItem item)
        {
            // Flush any pending debounced save before switching items
            if (_saveSchedule != null && _current != null && _current != item)
            {
                _saveSchedule.Pause();
                OnResourceUpdated?.Invoke(_current);
                _saveSchedule = null;
            }
            _current = item;
            if (item == null)
            {
                _content.style.display = DisplayStyle.None;
                _empty.style.display = DisplayStyle.Flex;
                return;
            }
            _content.style.display = DisplayStyle.Flex;
            _empty.style.display = DisplayStyle.None;

            _iconContainer.Clear();
            _iconContainer.Add(FileIconGenerator.CreateDetail(item));
            _nameLabel.text = item.Name;
            _typeLabel.text = $"{ContentGridView.GetTypeLabel(item.Type)}  |  {item.FileName}";
            _sizeVal.text = item.FileSizeDisplay;
            _dateVal.text = DateTime.TryParse(item.ImportedAt, out DateTime dt) ? dt.ToString("yyyy-MM-dd HH:mm") : "-";
            _pathVal.text = item.IsReference ? item.OriginalPath : item.RelativePath;
            _pathVal.tooltip = _pathVal.text;

            _versionField.SetValueWithoutNotify(item.Version ?? "");
            _urlField.SetValueWithoutNotify(item.SourceUrl ?? "");
            _descField.SetValueWithoutNotify(item.Description ?? "");

            #if UNITY_EDITOR
            _importUnityBtn.style.display = item.Type == ResourceType.UnityPackage ? DisplayStyle.Flex : DisplayStyle.None;
            #endif

            UpdateStars(item.Rating);
            RefreshTags();
            UpdateCoverPreview();
        }

        private void UpdateStars(int rating)
        {
            int i = 0;
            foreach (var c in _starRow.Children())
            {
                bool on = i < rating;
                c.EnableInClassList("mb-star--on", on);
                ((VisualElement)c).style.color = on ? new Color(1f, 0.84f, 0f) : new Color(0.48f, 0.55f, 0.63f);
                i++;
            }
        }

        private void SetRating(int r)
        {
            if (_current == null) return;
            _current.Rating = _current.Rating == r ? 0 : r;
            UpdateStars(_current.Rating);
            OnResourceUpdated?.Invoke(_current);
        }

        /// <summary>Debounce text field saves to avoid a DB write per keystroke.</summary>
        private void ScheduleSave()
        {
            _saveSchedule?.Pause();
            _saveSchedule = _content.schedule.Execute(() =>
            {
                if (_current != null) OnResourceUpdated?.Invoke(_current);
                _saveSchedule = null;
            }).StartingIn(SaveDelayMs);
        }

        private void RefreshTags()
        {
            _tagsContainer.Clear();
            if (_current == null) return;
            var itemTags = _tags.GetTagsForResource(_current.Id);
            foreach (var tag in itemTags)
            {
                var chip = new VisualElement(); chip.AddToClassList("mb-tag-chip");
                if (ColorUtility.TryParseHtmlString(tag.Color, out Color c))
                {
                    chip.style.backgroundColor = new Color(c.r, c.g, c.b, 0.12f);
                    chip.style.borderTopColor = chip.style.borderBottomColor =
                        chip.style.borderLeftColor = chip.style.borderRightColor = new Color(c.r, c.g, c.b, 0.35f);
                }
                var lbl = new Label(tag.Name); lbl.style.fontSize = 10; lbl.style.color = new Color(0.8f, 0.8f, 0.8f); chip.Add(lbl);

                var rm = new Button(() =>
                {
                    _tags.RemoveTagFromResources(new List<string> { _current.Id }, tag.Id);
                    _current.TagIds.Remove(tag.Id);
                    RefreshTags();
                }) { text = "\u00D7" };
                rm.style.fontSize = 10; rm.style.width = 14; rm.style.height = 14; rm.style.marginLeft = 4;
                rm.style.borderTopWidth = rm.style.borderBottomWidth = rm.style.borderLeftWidth = rm.style.borderRightWidth = 0;
                rm.style.backgroundColor = Color.clear; rm.style.color = new Color(0.6f, 0.6f, 0.6f);
                rm.style.paddingTop = rm.style.paddingBottom = rm.style.paddingLeft = rm.style.paddingRight = 0;
                chip.Add(rm);
                _tagsContainer.Add(chip);
            }
        }

        private void ShowAddTagMenu()
        {
            if (_current == null) return;
            var ctx = new ContextMenuPopup();
            var allTags = _tags.GetAllTags();
            var items = new List<ContextMenuPopup.MenuItem>();
            foreach (var t in allTags.Where(t => !_current.TagIds.Contains(t.Id)))
            {
                var tag = t;
                items.Add(new ContextMenuPopup.MenuItem(tag.Name, () =>
                {
                    _tags.AddTagToResources(new List<string> { _current.Id }, tag.Id);
                    _current.TagIds.Add(tag.Id);
                    RefreshTags();
                }));
            }
            items.Add(ContextMenuPopup.MenuItem.Separator());
            items.Add(new ContextMenuPopup.MenuItem(Loc.T("detail.create_tag"), () =>
            {
                _dialog.Show(_dialogRoot, Loc.T("detail.new_tag_title"), Loc.T("detail.new_tag_prompt"), "", name =>
                {
                    var tag = _tags.CreateTag(name);
                    if (tag != null) { _tags.AddTagToResources(new List<string> { _current.Id }, tag.Id); _current.TagIds.Add(tag.Id); RefreshTags(); }
                });
            }));
            var btnRect = _addTagBtn.worldBound;
            var rootRect = _dialogRoot.worldBound;
            var pos = new Vector2(btnRect.x - rootRect.x, btnRect.yMax - rootRect.y);
            ctx.Show(_dialogRoot, pos, items);
        }

        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" };

        public bool TryHandleDrop(string[] paths, Vector2 screenPoint)
        {
            if (_current == null || _content.style.display == DisplayStyle.None) return false;
            if (paths == null || paths.Length == 0) return false;

            // Check if any dropped file is an image
            string imagePath = null;
            foreach (var p in paths)
            {
                if (ImageExtensions.Contains(Path.GetExtension(p)))
                {
                    imagePath = p;
                    break;
                }
            }
            if (imagePath == null) return false;

            // Check if the drop point is over the cover preview area
            var coverRect = _coverPreview.worldBound;
            if (!coverRect.Contains(screenPoint)) return false;

            SetCoverImage(imagePath);
            return true;
        }

        private void SelectCoverImage()
        {
            if (_current == null || !_lib.IsLibraryOpen) return;
            var paths = Win32Dialog.OpenFileDialog(Loc.T("detail.cover_dialog"), Loc.T("detail.cover_filter"), false);
            if (paths == null || paths.Length == 0) return;
            SetCoverImage(paths[0]);
        }

        private void SetCoverImage(string imagePath)
        {
            if (_current == null || !_lib.IsLibraryOpen) return;
            string thumbDir = _lib.GetThumbnailsDirectory();
            if (string.IsNullOrEmpty(thumbDir)) return;
            if (!Directory.Exists(thumbDir)) Directory.CreateDirectory(thumbDir);

            string ext = Path.GetExtension(imagePath);
            string destName = $"{_current.Id}{ext}";
            string destPath = Path.Combine(thumbDir, destName);

            try { File.Copy(imagePath, destPath, true); }
            catch (Exception e)
            {
                Debug.LogWarning($"[MagicBox] Copy cover image failed: {e.Message}");
                return;
            }

            string oldThumb = _current.ThumbnailPath;
            _current.ThumbnailPath = destName;
            UpdateCoverPreview();
            OnCoverChanged?.Invoke(oldThumb);
            OnResourceUpdated?.Invoke(_current);
        }

        private void ClearCoverImage()
        {
            if (_current == null) return;
            if (!string.IsNullOrEmpty(_current.ThumbnailPath) && _lib.IsLibraryOpen)
            {
                string thumbDir = _lib.GetThumbnailsDirectory();
                if (!string.IsNullOrEmpty(thumbDir))
                {
                    string fullPath = Path.Combine(thumbDir, _current.ThumbnailPath);
                    if (File.Exists(fullPath)) try { File.Delete(fullPath); } catch { }
                }
            }
            string oldThumb = _current.ThumbnailPath;
            _current.ThumbnailPath = "";
            UpdateCoverPreview();
            OnCoverChanged?.Invoke(oldThumb);
            OnResourceUpdated?.Invoke(_current);
        }

        private void UpdateCoverPreview()
        {
            if (_coverTexture != null) { UnityEngine.Object.Destroy(_coverTexture); _coverTexture = null; }
            _coverPreview.Clear();

            if (_current != null && !string.IsNullOrEmpty(_current.ThumbnailPath) && _lib.IsLibraryOpen)
            {
                string thumbDir = _lib.GetThumbnailsDirectory();
                if (!string.IsNullOrEmpty(thumbDir))
                {
                    string fullPath = Path.Combine(thumbDir, _current.ThumbnailPath);
                    if (File.Exists(fullPath))
                    {
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(fullPath);
                            _coverTexture = new Texture2D(2, 2);
                            _coverTexture.LoadImage(bytes);
                            var img = new Image();
                            img.image = _coverTexture;
                            img.scaleMode = ScaleMode.ScaleToFit;
                            img.style.width = Length.Percent(100);
                            img.style.height = Length.Percent(100);
                            _coverPreview.Add(img);
                            return;
                        }
                        catch { }
                    }
                }
            }

            var hint = new Label(Loc.T("detail.cover_hint"));
            hint.style.fontSize = 11;
            hint.style.color = new Color(0.48f, 0.55f, 0.63f);
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            _coverPreview.Add(hint);
        }

        private VisualElement Section(string title)
        {
            var s = new VisualElement(); s.AddToClassList("mb-detail-section");
            var t = new Label(title); t.AddToClassList("mb-detail-section-title"); s.Add(t);
            return s;
        }

        private Label Field(VisualElement parent, string label)
        {
            var row = new VisualElement(); row.AddToClassList("mb-detail-row"); parent.Add(row);
            var lbl = new Label(label); lbl.AddToClassList("mb-detail-label"); row.Add(lbl);
            var val = new Label("-"); val.AddToClassList("mb-detail-value"); row.Add(val);
            return val;
        }

        private TextField TextInput(VisualElement parent, string label, Action<string> onChange)
        {
            var lbl = new Label(label); lbl.style.fontSize = 11; lbl.style.color = new Color(0.48f, 0.55f, 0.63f); lbl.style.marginBottom = 2; parent.Add(lbl);
            var field = new TextField(); field.AddToClassList("mb-input-field"); field.style.marginBottom = 8;
            field.RegisterValueChangedCallback(e => onChange?.Invoke(e.newValue));
            parent.Add(field);
            return field;
        }
    }
}
