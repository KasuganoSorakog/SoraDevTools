using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MagicBox.Editor
{
    public class MarkdownBrowserWindow : EditorWindow
    {
        // --- Data ---
        private List<MarkdownFileInfo> _allFiles = new List<MarkdownFileInfo>();
        private List<MarkdownFileInfo> _filteredFiles = new List<MarkdownFileInfo>();
        private Dictionary<string, bool> _folderFoldouts = new Dictionary<string, bool>();

        // --- Clickable links ---
        private struct LinkInfo
        {
            public Rect Rect;
            public string Url;
        }
        private List<LinkInfo> _links = new List<LinkInfo>();

        // --- UI State ---
        private string _searchText = "";
        private Vector2 _listScroll;
        private Vector2 _contentScroll;
        private int _selectedIndex = -1;
        private string _selectedContent = "";
        private string _selectedFilePath = "";
        private float _splitterRatio = 0.35f;
        private bool _isDraggingSplitter;
        private float _contentAreaX; // track content panel origin for link offsets

        // --- Styles (lazy init) ---
        private bool _stylesInited;
        private GUIStyle _toolbarStyle;
        private GUIStyle _fileItemStyle;
        private GUIStyle _fileItemSelectedStyle;
        private GUIStyle _folderLabelStyle;
        private GUIStyle _fileNameStyle;
        private GUIStyle _filePathStyle;
        private GUIStyle _contentStyle;
        private GUIStyle _headingStyle;
        private GUIStyle _subHeadingStyle;
        private GUIStyle _h3Style;
        private GUIStyle _h4Style;
        private GUIStyle _codeBlockStyle;
        private GUIStyle _separatorStyle;
        private GUIStyle _bulletStyle;
        private GUIStyle _boldStyle;
        private GUIStyle _statsLabelStyle;
        private GUIStyle _emptyStateStyle;
        private GUIStyle _tableCellStyle;
        private GUIStyle _tableHeaderStyle;
        private GUIStyle _linkStyle;

        // --- Colors ---
        private static readonly Color AccentColor = new Color(0.35f, 0.65f, 0.95f);
        private static readonly Color SelectedBg = new Color(0.35f, 0.65f, 0.95f, 0.25f);
        private static readonly Color HoverBg = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color SplitterColor = new Color(0.15f, 0.15f, 0.15f);
        private static readonly Color SidebarBg = new Color(0.19f, 0.19f, 0.19f);
        private static readonly Color ContentBg = new Color(0.22f, 0.22f, 0.22f);
        private static readonly Color CodeBg = new Color(0.14f, 0.14f, 0.14f);
        private static readonly Color FolderIconColor = new Color(0.95f, 0.78f, 0.35f);

        // Syntax highlight colors
        private static readonly string SynKeyword = "#569CD6";   // blue
        private static readonly string SynString = "#CE9178";    // orange
        private static readonly string SynComment = "#6A9955";   // green
        private static readonly string SynNumber = "#B5CEA8";    // light green
        private static readonly string SynType = "#4EC9B0";      // teal
        private static readonly string SynMethod = "#DCDCAA";    // yellow
        private static readonly string SynDefault = "#D4D4D4";   // light gray

        // Table colors
        private static readonly Color TableHeaderBg = new Color(0.18f, 0.22f, 0.30f);
        private static readonly Color TableRowEvenBg = new Color(0.16f, 0.16f, 0.18f);
        private static readonly Color TableRowOddBg = new Color(0.19f, 0.19f, 0.21f);
        private static readonly Color TableBorderColor = new Color(0.30f, 0.30f, 0.35f);

        // Heading underline colors
        private static readonly Color H1UnderlineColor = new Color(0.35f, 0.65f, 0.95f);
        private static readonly Color H2UnderlineColor = new Color(0.45f, 0.55f, 0.75f, 0.6f);
        private static readonly Color H3UnderlineColor = new Color(0.45f, 0.50f, 0.65f, 0.4f);
        private static readonly Color H4UnderlineColor = new Color(0.45f, 0.50f, 0.60f, 0.3f);

        private struct MarkdownFileInfo
        {
            public string FullPath;
            public string RelativePath;
            public string FileName;
            public string FolderPath;
            public string FolderDisplay;
            public long FileSize;
            public System.DateTime LastModified;
        }

        // C# keywords for syntax highlighting
        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>
        {
            "abstract","as","base","bool","break","byte","case","catch","char","checked",
            "class","const","continue","decimal","default","delegate","do","double","else",
            "enum","event","explicit","extern","false","finally","fixed","float","for",
            "foreach","goto","if","implicit","in","int","interface","internal","is","lock",
            "long","namespace","new","null","object","operator","out","override","params",
            "private","protected","public","readonly","ref","return","sbyte","sealed","short",
            "sizeof","stackalloc","static","string","struct","switch","this","throw","true",
            "try","typeof","uint","ulong","unchecked","unsafe","ushort","using","var",
            "virtual","void","volatile","while","async","await","yield","get","set","value",
            "partial","where","from","select","orderby","group","into","join","let","on",
            "equals","ascending","descending"
        };

        private static readonly HashSet<string> CSharpTypes = new HashSet<string>
        {
            "String","Int32","Int64","Boolean","Single","Double","Byte","Char","Decimal",
            "Object","DateTime","TimeSpan","List","Dictionary","HashSet","Array","Action",
            "Func","Task","IEnumerable","IEnumerator","IList","IDictionary","IDisposable",
            "Vector2","Vector3","Vector4","Quaternion","Color","Rect","Bounds",
            "GameObject","Transform","MonoBehaviour","ScriptableObject","Component",
            "Rigidbody","Collider","Renderer","Material","Texture","Sprite","AudioSource",
            "Camera","Light","Canvas","Image","Text","Button","Debug","EditorWindow",
            "Editor","SerializedProperty","SerializedObject","GUILayout","EditorGUILayout",
            "GUIStyle","GUIContent"
        };

        [MenuItem("SoraTools/Markdown Browser &g")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<MarkdownBrowserWindow>();
            wnd.titleContent = new GUIContent("Markdown Browser", EditorGUIUtility.IconContent("TextAsset Icon").image);
            wnd.minSize = new Vector2(700, 400);
            wnd.Show();
        }

        private void OnEnable()
        {
            RefreshFileList();
            SelectFirstFile();
        }

        private void SelectFirstFile()
        {
            if (_filteredFiles.Count > 0)
            {
                _selectedIndex = 0;
                LoadFileContent(_filteredFiles[0].FullPath);

                // Ensure the folder containing the first file is expanded
                string folder = _filteredFiles[0].FolderDisplay;
                if (_folderFoldouts.ContainsKey(folder))
                    _folderFoldouts[folder] = true;
            }
        }

        // ======================= FILE SCANNING =======================

        private void RefreshFileList()
        {
            _allFiles.Clear();
            _folderFoldouts.Clear();

            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            ScanDirectory(Path.Combine(projectRoot, "Assets"), projectRoot);

            string packagesPath = Path.Combine(projectRoot, "Packages");
            if (Directory.Exists(packagesPath))
                ScanDirectory(packagesPath, projectRoot);

            string cachePath = Path.Combine(projectRoot, "Library", "PackageCache");
            if (Directory.Exists(cachePath))
                ScanDirectory(cachePath, projectRoot);

            _allFiles = _allFiles.OrderBy(f => f.FolderDisplay).ThenBy(f => f.FileName).ToList();

            foreach (var f in _allFiles)
            {
                if (!_folderFoldouts.ContainsKey(f.FolderDisplay))
                    _folderFoldouts[f.FolderDisplay] = f.FolderDisplay.StartsWith("Assets");
            }

            ApplyFilter();
        }

        private void ScanDirectory(string rootDir, string projectRoot)
        {
            if (!Directory.Exists(rootDir)) return;

            try
            {
                foreach (string filePath in Directory.GetFiles(rootDir, "*.md", SearchOption.AllDirectories))
                {
                    string relative = filePath.Substring(projectRoot.Length + 1).Replace('\\', '/');

                    // Skip com.unity official packages
                    string relLower = relative.Replace('\\', '/').ToLowerInvariant();
                    if (relLower.Contains("com.unity."))
                        continue;

                    var fi = new FileInfo(filePath);

                    string folderRel = Path.GetDirectoryName(relative).Replace('\\', '/');

                    string folderDisplay = folderRel;
                    if (folderDisplay.StartsWith("Library/PackageCache/"))
                    {
                        string sub = folderDisplay.Substring("Library/PackageCache/".Length);
                        int atIdx = sub.IndexOf('@');
                        string pkgName = atIdx > 0 ? sub.Substring(0, atIdx) : sub;
                        int slashIdx = sub.IndexOf('/');
                        string remainder = slashIdx > 0 ? sub.Substring(slashIdx) : "";
                        folderDisplay = "Packages (Cache)/" + pkgName + remainder;
                    }

                    _allFiles.Add(new MarkdownFileInfo
                    {
                        FullPath = filePath,
                        RelativePath = relative,
                        FileName = Path.GetFileName(filePath),
                        FolderPath = folderRel,
                        FolderDisplay = folderDisplay,
                        FileSize = fi.Length,
                        LastModified = fi.LastWriteTime,
                    });
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MarkdownBrowser] Error scanning {rootDir}: {e.Message}");
            }
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrEmpty(_searchText))
            {
                _filteredFiles = new List<MarkdownFileInfo>(_allFiles);
            }
            else
            {
                string lower = _searchText.ToLowerInvariant();
                _filteredFiles = _allFiles
                    .Where(f => f.FileName.ToLowerInvariant().Contains(lower)
                             || f.RelativePath.ToLowerInvariant().Contains(lower))
                    .ToList();
            }
        }

        // ======================= STYLES =======================

        private void InitStyles()
        {
            if (_stylesInited) return;
            _stylesInited = true;

            _toolbarStyle = new GUIStyle(EditorStyles.toolbar) { fixedHeight = 30 };

            _fileItemStyle = new GUIStyle("CN EntryBackOdd")
            {
                padding = new RectOffset(8, 8, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
                fixedHeight = 0,
                stretchWidth = true,
            };

            _fileItemSelectedStyle = new GUIStyle(_fileItemStyle);

            _folderLabelStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold, fontSize = 13 };
            _folderLabelStyle.normal.textColor = FolderIconColor;
            _folderLabelStyle.onNormal.textColor = FolderIconColor;
            _folderLabelStyle.focused.textColor = FolderIconColor;
            _folderLabelStyle.onFocused.textColor = FolderIconColor;
            _folderLabelStyle.active.textColor = FolderIconColor;
            _folderLabelStyle.onActive.textColor = FolderIconColor;

            _fileNameStyle = new GUIStyle(EditorStyles.label) { fontSize = 14, fontStyle = FontStyle.Normal };
            _fileNameStyle.normal.textColor = Color.white;

            _filePathStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11 };
            _filePathStyle.normal.textColor = new Color(1, 1, 1, 0.45f);

            _contentStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true, richText = true, fontSize = 14,
                padding = new RectOffset(16, 16, 4, 4),
            };
            _contentStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

            _headingStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 24, fontStyle = FontStyle.Bold, wordWrap = true, richText = true,
                padding = new RectOffset(16, 16, 12, 4),
            };
            _headingStyle.normal.textColor = Color.white;

            _subHeadingStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = true, richText = true,
                padding = new RectOffset(16, 16, 10, 2),
            };
            _subHeadingStyle.normal.textColor = new Color(0.9f, 0.9f, 0.95f);

            _h3Style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = true, richText = true,
                padding = new RectOffset(16, 16, 8, 2),
            };
            _h3Style.normal.textColor = new Color(0.85f, 0.88f, 0.95f);

            _h4Style = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true, richText = true, fontSize = 14, fontStyle = FontStyle.Bold,
                padding = new RectOffset(16, 16, 6, 2),
            };
            _h4Style.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

            _codeBlockStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = false, richText = true, fontSize = 13,
                padding = new RectOffset(12, 12, 8, 8),
                margin = new RectOffset(0, 0, 0, 0),
            };
            _codeBlockStyle.normal.textColor = new Color(0.83f, 0.83f, 0.83f);

            _separatorStyle = new GUIStyle() { fixedHeight = 1, margin = new RectOffset(16, 16, 8, 8) };

            _bulletStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true, richText = true, fontSize = 14,
                padding = new RectOffset(28, 16, 2, 2),
            };
            _bulletStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

            _boldStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true, richText = true, fontSize = 14, fontStyle = FontStyle.Bold,
                padding = new RectOffset(16, 16, 4, 4),
            };
            _boldStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

            _statsLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 };

            _emptyStateStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 14, wordWrap = true,
            };
            _emptyStateStyle.normal.textColor = new Color(1, 1, 1, 0.4f);

            _tableCellStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true, richText = true, fontSize = 13,
                padding = new RectOffset(8, 8, 4, 4),
                alignment = TextAnchor.MiddleLeft,
            };
            _tableCellStyle.normal.textColor = new Color(0.82f, 0.82f, 0.82f);

            _tableHeaderStyle = new GUIStyle(_tableCellStyle)
            {
                fontStyle = FontStyle.Bold,
            };
            _tableHeaderStyle.normal.textColor = new Color(0.95f, 0.88f, 0.65f);

            _linkStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true, richText = true, fontSize = 14,
                padding = new RectOffset(0, 0, 0, 0),
            };
            _linkStyle.normal.textColor = new Color(0.35f, 0.62f, 0.83f);
        }

        // ======================= MAIN GUI =======================

        private void OnGUI()
        {
            InitStyles();

            // Handle link clicks at the start of frame (before layout)
            HandleLinkClicks();

            DrawToolbar();

            Rect body = new Rect(0, 30, position.width, position.height - 30);
            float splitterWidth = 4f;
            float listWidth = body.width * _splitterRatio;
            float contentWidth = body.width - listWidth - splitterWidth;

            EditorGUI.DrawRect(new Rect(body.x, body.y, listWidth, body.height), SidebarBg);
            EditorGUI.DrawRect(new Rect(body.x + listWidth + splitterWidth, body.y, contentWidth, body.height), ContentBg);

            Rect listRect = new Rect(body.x, body.y, listWidth, body.height);
            DrawFileList(listRect);

            Rect splitterRect = new Rect(body.x + listWidth, body.y, splitterWidth, body.height);
            EditorGUI.DrawRect(splitterRect, SplitterColor);
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
            HandleSplitter(splitterRect, body);

            Rect contentRect = new Rect(body.x + listWidth + splitterWidth, body.y, contentWidth, body.height);
            _contentAreaX = contentRect.x;
            DrawContentPanel(contentRect);
        }

        private void HandleLinkClicks()
        {
            if (Event.current.type != EventType.MouseDown || Event.current.button != 0)
                return;

            Vector2 mp = Event.current.mousePosition;
            for (int i = _links.Count - 1; i >= 0; i--)
            {
                if (_links[i].Rect.Contains(mp))
                {
                    Application.OpenURL(_links[i].Url);
                    Event.current.Use();
                    return;
                }
            }
        }

        private void HandleSplitter(Rect splitterRect, Rect body)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && splitterRect.Contains(e.mousePosition))
            {
                _isDraggingSplitter = true;
                e.Use();
            }
            if (_isDraggingSplitter)
            {
                if (e.type == EventType.MouseDrag)
                {
                    _splitterRatio = Mathf.Clamp((e.mousePosition.x - body.x) / body.width, 0.15f, 0.6f);
                    Repaint();
                    e.Use();
                }
                if (e.type == EventType.MouseUp)
                {
                    _isDraggingSplitter = false;
                    e.Use();
                }
            }
        }

        // ======================= TOOLBAR =======================

        private void DrawToolbar()
        {
            Rect toolbarRect = new Rect(0, 0, position.width, 30);
            EditorGUI.DrawRect(toolbarRect, new Color(0.17f, 0.17f, 0.17f));

            GUILayout.BeginArea(toolbarRect);
            GUILayout.BeginHorizontal();
            GUILayout.Space(8);

            GUILayout.Label(EditorGUIUtility.IconContent("Search Icon"), GUILayout.Width(20), GUILayout.Height(28));
            EditorGUI.BeginChangeCheck();
            _searchText = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(150), GUILayout.Height(20));
            if (EditorGUI.EndChangeCheck())
                ApplyFilter();

            GUILayout.FlexibleSpace();
            GUILayout.Label($"{_filteredFiles.Count} / {_allFiles.Count} files", _statsLabelStyle, GUILayout.Height(28));
            GUILayout.Space(8);

            if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Refresh"),
                EditorStyles.toolbarButton, GUILayout.Width(28), GUILayout.Height(28)))
            {
                _selectedIndex = -1;
                _selectedContent = "";
                _selectedFilePath = "";
                RefreshFileList();
            }
            GUILayout.Space(4);

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ======================= FILE LIST =======================

        private void DrawFileList(Rect rect)
        {
            if (_filteredFiles.Count == 0)
            {
                GUI.BeginGroup(rect);
                GUI.Label(new Rect(20, rect.height * 0.4f, rect.width - 40, 40),
                    string.IsNullOrEmpty(_searchText) ? "No .md files found" : "No matching files",
                    _emptyStateStyle);
                GUI.EndGroup();
                return;
            }

            var groups = _filteredFiles.GroupBy(f => f.FolderDisplay).OrderBy(g => g.Key).ToList();

            GUILayout.BeginArea(rect);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            GUILayout.Space(4);

            foreach (var group in groups)
            {
                if (!_folderFoldouts.ContainsKey(group.Key))
                    _folderFoldouts[group.Key] = false;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(4);
                _folderFoldouts[group.Key] = EditorGUILayout.Foldout(_folderFoldouts[group.Key],
                    $"  {group.Key}  ({group.Count()})", true, _folderLabelStyle);
                EditorGUILayout.EndHorizontal();

                if (!_folderFoldouts[group.Key]) continue;

                foreach (var file in group)
                {
                    int idx = _filteredFiles.IndexOf(file);
                    bool isSelected = (idx == _selectedIndex);

                    Rect itemRect = EditorGUILayout.BeginVertical();

                    if (isSelected)
                    {
                        EditorGUI.DrawRect(itemRect, SelectedBg);
                        EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.y, 3, itemRect.height), AccentColor);
                    }
                    else if (itemRect.Contains(Event.current.mousePosition))
                    {
                        EditorGUI.DrawRect(itemRect, HoverBg);
                    }

                    GUILayout.Space(3);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    GUILayout.Label(EditorGUIUtility.IconContent("TextAsset Icon"), GUILayout.Width(16), GUILayout.Height(16));
                    GUILayout.Space(4);

                    EditorGUILayout.BeginVertical();
                    GUILayout.Label(file.FileName, _fileNameStyle);
                    string sizeStr = file.FileSize < 1024 ? $"{file.FileSize} B"
                        : file.FileSize < 1048576 ? $"{file.FileSize / 1024f:F1} KB"
                        : $"{file.FileSize / 1048576f:F1} MB";
                    GUILayout.Label($"{sizeStr}  \u00b7  {file.LastModified:yyyy-MM-dd}", _filePathStyle);
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(3);
                    EditorGUILayout.EndVertical();

                    if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
                    {
                        _selectedIndex = idx;
                        LoadFileContent(file.FullPath);
                        Event.current.Use();
                        Repaint();
                    }
                }
                GUILayout.Space(2);
            }

            GUILayout.Space(8);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void LoadFileContent(string fullPath)
        {
            _selectedFilePath = fullPath;
            try { _selectedContent = File.ReadAllText(fullPath); }
            catch (System.Exception e) { _selectedContent = $"Error reading file:\n{e.Message}"; }
        }

        // ======================= CONTENT PANEL =======================

        private void DrawContentPanel(Rect rect)
        {
            if (string.IsNullOrEmpty(_selectedContent))
            {
                GUI.BeginGroup(rect);
                GUI.Label(new Rect(20, rect.height * 0.4f, rect.width - 40, 60),
                    "Select a Markdown file to preview", _emptyStateStyle);
                GUI.EndGroup();
                return;
            }

            GUILayout.BeginArea(rect);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            string display = _selectedFilePath;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (display.StartsWith(projectRoot))
                display = display.Substring(projectRoot.Length + 1).Replace('\\', '/');
            GUILayout.Label(display, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Open External", EditorStyles.toolbarButton, GUILayout.Width(90)))
                System.Diagnostics.Process.Start(_selectedFilePath);
            if (GUILayout.Button("Copy Path", EditorStyles.toolbarButton, GUILayout.Width(70)))
                EditorGUIUtility.systemCopyBuffer = _selectedFilePath;
            EditorGUILayout.EndHorizontal();

            _contentScroll = EditorGUILayout.BeginScrollView(_contentScroll);
            GUILayout.Space(8);

            // Clear links each frame and rebuild during Repaint
            if (Event.current.type == EventType.Repaint)
                _links.Clear();

            RenderMarkdown(_selectedContent, rect.width - 30);

            GUILayout.Space(20);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ======================= MARKDOWN RENDERER =======================

        private void RenderMarkdown(string markdown, float width)
        {
            if (string.IsNullOrEmpty(markdown)) return;

            string[] lines = markdown.Split('\n');
            bool inCodeBlock = false;
            string codeLanguage = "";
            List<string> codeLines = new List<string>();
            List<string[]> tableRows = new List<string[]>();
            bool inTable = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');

                // Code block toggle
                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        FlushTable(ref tableRows, ref inTable, width);
                        string code = string.Join("\n", codeLines);
                        DrawCodeBlock(code, codeLanguage, width);
                        codeLines.Clear();
                        inCodeBlock = false;
                        codeLanguage = "";
                    }
                    else
                    {
                        FlushTable(ref tableRows, ref inTable, width);
                        inCodeBlock = true;
                        string tag = line.TrimStart().Substring(3).Trim();
                        codeLanguage = tag.ToLowerInvariant();
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeLines.Add(line);
                    continue;
                }

                // Table detection
                string trimmedForTable = line.Trim();
                if (trimmedForTable.StartsWith("|") && trimmedForTable.EndsWith("|"))
                {
                    // Check if it's a separator row like |---|---|
                    if (IsTableSeparator(trimmedForTable))
                    {
                        inTable = true;
                        continue; // skip separator row
                    }

                    string[] cells = ParseTableRow(trimmedForTable);
                    tableRows.Add(cells);
                    inTable = true;
                    continue;
                }
                else
                {
                    FlushTable(ref tableRows, ref inTable, width);
                }

                // Empty line
                if (string.IsNullOrWhiteSpace(line))
                {
                    GUILayout.Space(6);
                    continue;
                }

                string trimmed = line.TrimStart();

                // Horizontal rule
                if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                {
                    DrawHorizontalRule(width);
                    continue;
                }

                // Headings
                if (trimmed.StartsWith("# "))
                {
                    GUILayout.Space(4);
                    DrawRichLabel(FormatInlineMarkdown(trimmed.Substring(2)), _headingStyle, width);
                    DrawHeadingUnderline(width, H1UnderlineColor, 2f, 1f);
                    GUILayout.Space(4);
                    continue;
                }
                if (trimmed.StartsWith("## "))
                {
                    GUILayout.Space(4);
                    DrawRichLabel(FormatInlineMarkdown(trimmed.Substring(3)), _subHeadingStyle, width);
                    DrawHeadingUnderline(width, H2UnderlineColor, 1f, 1f);
                    GUILayout.Space(4);
                    continue;
                }
                if (trimmed.StartsWith("### "))
                {
                    GUILayout.Space(2);
                    DrawRichLabel(FormatInlineMarkdown(trimmed.Substring(4)), _h3Style, width);
                    DrawHeadingUnderline(width, H3UnderlineColor, 1f, 0.7f);
                    GUILayout.Space(3);
                    continue;
                }
                if (trimmed.StartsWith("#### "))
                {
                    GUILayout.Space(2);
                    DrawRichLabel($"<b>{FormatInlineMarkdown(trimmed.Substring(5))}</b>", _h4Style, width);
                    DrawHeadingUnderline(width, H4UnderlineColor, 1f, 0.5f);
                    GUILayout.Space(2);
                    continue;
                }

                // Bullet points
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
                {
                    DrawRichLabel($"  \u2022  {FormatInlineMarkdown(trimmed.Substring(2))}", _bulletStyle, width);
                    continue;
                }

                // Numbered list
                if (trimmed.Length > 2 && char.IsDigit(trimmed[0]) && trimmed.Contains(". "))
                {
                    int dotIdx = trimmed.IndexOf(". ");
                    if (dotIdx > 0 && dotIdx <= 3)
                    {
                        string num = trimmed.Substring(0, dotIdx);
                        string text = trimmed.Substring(dotIdx + 2);
                        DrawRichLabel($"  {num}.  {FormatInlineMarkdown(text)}", _bulletStyle, width);
                        continue;
                    }
                }

                // Blockquote
                if (trimmed.StartsWith("> "))
                {
                    Rect quoteRect = EditorGUILayout.BeginVertical();
                    EditorGUI.DrawRect(new Rect(quoteRect.x + 16, quoteRect.y, 3, quoteRect.height), AccentColor);
                    var quoteStyle = new GUIStyle(_contentStyle) { padding = new RectOffset(28, 16, 2, 2) };
                    quoteStyle.normal.textColor = new Color(0.75f, 0.8f, 0.9f);
                    DrawRichLabel($"<i>{FormatInlineMarkdown(trimmed.Substring(2))}</i>", quoteStyle, width);
                    EditorGUILayout.EndVertical();
                    continue;
                }

                // Regular paragraph - check for links
                DrawParagraphWithLinks(trimmed, width);
            }

            // Flush remaining
            FlushTable(ref tableRows, ref inTable, width);

            if (inCodeBlock && codeLines.Count > 0)
            {
                string code = string.Join("\n", codeLines);
                DrawCodeBlock(code, codeLanguage, width);
            }
        }

        // ======================= CLICKABLE LINKS =======================

        private void DrawParagraphWithLinks(string line, float width)
        {
            // Check if line contains markdown links
            var linkMatches = Regex.Matches(line, @"\[([^\]]+)\]\(([^)]+)\)");
            if (linkMatches.Count == 0)
            {
                DrawRichLabel(FormatInlineMarkdown(line), _contentStyle, width);
                return;
            }

            // Build the display text with link placeholders
            string formatted = FormatInlineMarkdown(line);
            DrawRichLabel(formatted, _contentStyle, width);

            // Extract link URLs from the original line for click registration
            // We register clickable areas using last rect
            Rect lastRect = GUILayoutUtility.GetLastRect();

            foreach (Match m in linkMatches)
            {
                string linkText = m.Groups[1].Value;
                string url = m.Groups[2].Value;

                // Register a clickable area for the full label area
                // (In IMGUI it's hard to get exact sub-string positions,
                //  so we add cursor hint for the whole label and register click on it)
                RegisterLink(lastRect, url, linkText, formatted);
            }
        }

        private void RegisterLink(Rect labelRect, string url, string linkText, string fullText)
        {
            // For simplicity, we make the entire label area clickable if it contains a link
            // and add the hand cursor. For multiple links in one line they share the rect.
            // In practice this works well since most lines have 0-1 links.
            if (Event.current.type == EventType.Repaint)
            {
                _links.Add(new LinkInfo { Rect = labelRect, Url = url });
            }
            EditorGUIUtility.AddCursorRect(labelRect, MouseCursor.Link);
        }

        // ======================= TABLE RENDERING =======================

        private bool IsTableSeparator(string line)
        {
            string inner = line.Trim('|').Trim();
            return Regex.IsMatch(inner, @"^[\s\-:|]+$") && inner.Contains("-");
        }

        private string[] ParseTableRow(string line)
        {
            string inner = line.Trim();
            if (inner.StartsWith("|")) inner = inner.Substring(1);
            if (inner.EndsWith("|")) inner = inner.Substring(0, inner.Length - 1);
            return inner.Split('|').Select(c => c.Trim()).ToArray();
        }

        private void FlushTable(ref List<string[]> rows, ref bool inTable, float width)
        {
            if (!inTable || rows.Count == 0) return;

            GUILayout.Space(4);
            DrawTable(rows, width);
            GUILayout.Space(4);

            rows.Clear();
            inTable = false;
        }

        private void DrawTable(List<string[]> rows, float availWidth)
        {
            if (rows.Count == 0) return;

            int colCount = rows.Max(r => r.Length);
            float tableWidth = availWidth - 32;
            float cellWidth = tableWidth / colCount;
            // Usable text width inside a cell (subtract padding: 8 left + 8 right + 1 separator)
            float cellContentWidth = cellWidth - 17f;

            float tableX = 16;

            for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                string[] row = rows[rowIdx];
                bool isHeader = (rowIdx == 0);
                GUIStyle style = isHeader ? _tableHeaderStyle : _tableCellStyle;

                // Calculate row height based on tallest cell
                float rowHeight = 28f; // minimum
                for (int c = 0; c < colCount; c++)
                {
                    string cellText = (c < row.Length) ? row[c] : "";
                    string formatted = FormatInlineMarkdown(cellText);
                    float h = style.CalcHeight(new GUIContent(formatted), cellContentWidth);
                    if (h > rowHeight) rowHeight = h;
                }

                Rect rowRect = GUILayoutUtility.GetRect(tableWidth, rowHeight);
                rowRect.x += tableX;
                rowRect.width = tableWidth;

                // Row background
                Color bgColor = isHeader ? TableHeaderBg
                    : (rowIdx % 2 == 0 ? TableRowEvenBg : TableRowOddBg);
                EditorGUI.DrawRect(rowRect, bgColor);

                // Bottom border
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1, rowRect.width, 1), TableBorderColor);

                // Cells
                for (int c = 0; c < colCount; c++)
                {
                    Rect cellRect = new Rect(rowRect.x + c * cellWidth, rowRect.y, cellWidth, rowRect.height);

                    // Vertical separator
                    if (c > 0)
                        EditorGUI.DrawRect(new Rect(cellRect.x, cellRect.y, 1, cellRect.height), TableBorderColor);

                    string cellText = (c < row.Length) ? row[c] : "";
                    string formatted = FormatInlineMarkdown(cellText);
                    GUI.Label(cellRect, formatted, style);
                }
            }
        }

        // ======================= CODE BLOCK WITH SYNTAX HIGHLIGHT =======================

        private void DrawCodeBlock(string code, string language, float width)
        {
            GUILayout.Space(4);

            // Highlight code
            string highlighted;
            if (language == "csharp" || language == "c#" || language == "cs")
                highlighted = HighlightCSharp(code);
            else if (language == "json")
                highlighted = HighlightJson(code);
            else if (language == "xml" || language == "html")
                highlighted = HighlightXml(code);
            else
                highlighted = HighlightGeneric(code);

            // Measure content
            var tempContent = new GUIContent(code); // plain text for measurement
            float codeHeight = Mathf.Max(_codeBlockStyle.CalcHeight(tempContent, width - 40), 30);

            // Background rect
            Rect bgRect = GUILayoutUtility.GetRect(width - 32, codeHeight + 16);
            bgRect.x += 16;
            bgRect.width = width - 48;

            // Draw rounded-ish background
            EditorGUI.DrawRect(bgRect, CodeBg);
            // Top accent line
            EditorGUI.DrawRect(new Rect(bgRect.x, bgRect.y, bgRect.width, 1),
                new Color(0.35f, 0.65f, 0.95f, 0.3f));

            // Language tag
            if (!string.IsNullOrEmpty(language))
            {
                var tagStyle = new GUIStyle(EditorStyles.miniLabel);
                tagStyle.normal.textColor = new Color(1, 1, 1, 0.3f);
                Rect tagRect = new Rect(bgRect.xMax - 60, bgRect.y + 2, 56, 14);
                GUI.Label(tagRect, language, tagStyle);
            }

            // Code content
            Rect codeRect = new Rect(bgRect.x + 12, bgRect.y + 8, bgRect.width - 24, bgRect.height - 16);
            GUI.Label(codeRect, highlighted, _codeBlockStyle);

            GUILayout.Space(4);
        }

        private string HighlightCSharp(string code)
        {
            // We process token by token using a simple state machine
            var result = new System.Text.StringBuilder();
            int i = 0;
            int len = code.Length;

            while (i < len)
            {
                char c = code[i];

                // Line comment
                if (c == '/' && i + 1 < len && code[i + 1] == '/')
                {
                    int end = code.IndexOf('\n', i);
                    if (end < 0) end = len;
                    result.Append($"<color={SynComment}>");
                    result.Append(EscapeRichText(code.Substring(i, end - i)));
                    result.Append("</color>");
                    i = end;
                    continue;
                }

                // Block comment
                if (c == '/' && i + 1 < len && code[i + 1] == '*')
                {
                    int end = code.IndexOf("*/", i + 2);
                    if (end < 0) end = len - 2;
                    end += 2;
                    result.Append($"<color={SynComment}>");
                    result.Append(EscapeRichText(code.Substring(i, end - i)));
                    result.Append("</color>");
                    i = end;
                    continue;
                }

                // String literal
                if (c == '"')
                {
                    int end = i + 1;
                    while (end < len && code[end] != '"')
                    {
                        if (code[end] == '\\') end++; // skip escaped char
                        end++;
                    }
                    if (end < len) end++; // include closing quote
                    result.Append($"<color={SynString}>");
                    result.Append(EscapeRichText(code.Substring(i, end - i)));
                    result.Append("</color>");
                    i = end;
                    continue;
                }

                // Char literal
                if (c == '\'')
                {
                    int end = i + 1;
                    while (end < len && code[end] != '\'')
                    {
                        if (code[end] == '\\') end++;
                        end++;
                    }
                    if (end < len) end++;
                    result.Append($"<color={SynString}>");
                    result.Append(EscapeRichText(code.Substring(i, end - i)));
                    result.Append("</color>");
                    i = end;
                    continue;
                }

                // Number
                if (char.IsDigit(c) && (i == 0 || !char.IsLetterOrDigit(code[i - 1]) && code[i - 1] != '_'))
                {
                    int end = i;
                    while (end < len && (char.IsDigit(code[end]) || code[end] == '.' || code[end] == 'f'
                           || code[end] == 'x' || code[end] == 'X'
                           || (code[end] >= 'a' && code[end] <= 'f')
                           || (code[end] >= 'A' && code[end] <= 'F')))
                        end++;
                    result.Append($"<color={SynNumber}>");
                    result.Append(EscapeRichText(code.Substring(i, end - i)));
                    result.Append("</color>");
                    i = end;
                    continue;
                }

                // Identifier / keyword
                if (char.IsLetter(c) || c == '_')
                {
                    int end = i;
                    while (end < len && (char.IsLetterOrDigit(code[end]) || code[end] == '_'))
                        end++;
                    string word = code.Substring(i, end - i);

                    if (CSharpKeywords.Contains(word))
                        result.Append($"<color={SynKeyword}>{EscapeRichText(word)}</color>");
                    else if (CSharpTypes.Contains(word) || (word.Length > 1 && char.IsUpper(word[0])
                             && end < len && code[end] != '(' && !word.All(ch => char.IsUpper(ch) || ch == '_')))
                    {
                        // Heuristic: PascalCase not followed by ( = type, unless ALL_CAPS
                        if (CSharpTypes.Contains(word))
                            result.Append($"<color={SynType}>{EscapeRichText(word)}</color>");
                        else
                            result.Append($"<color={SynDefault}>{EscapeRichText(word)}</color>");
                    }
                    else if (end < len && code[end] == '(')
                        result.Append($"<color={SynMethod}>{EscapeRichText(word)}</color>");
                    else
                        result.Append($"<color={SynDefault}>{EscapeRichText(word)}</color>");

                    i = end;
                    continue;
                }

                // Newline - pass through
                if (c == '\n')
                {
                    result.Append('\n');
                    i++;
                    continue;
                }

                // Other characters (operators, braces, etc.)
                result.Append(EscapeRichText(c.ToString()));
                i++;
            }

            return result.ToString();
        }

        private string HighlightJson(string code)
        {
            var result = new System.Text.StringBuilder();
            int i = 0;
            int len = code.Length;

            while (i < len)
            {
                char c = code[i];

                if (c == '"')
                {
                    int end = i + 1;
                    while (end < len && code[end] != '"')
                    {
                        if (code[end] == '\\') end++;
                        end++;
                    }
                    if (end < len) end++;
                    string s = code.Substring(i, end - i);

                    // Check if it's a key (followed by :)
                    int afterQuote = end;
                    while (afterQuote < len && code[afterQuote] == ' ') afterQuote++;
                    bool isKey = afterQuote < len && code[afterQuote] == ':';

                    string color = isKey ? SynType : SynString;
                    result.Append($"<color={color}>{EscapeRichText(s)}</color>");
                    i = end;
                    continue;
                }

                if (char.IsDigit(c) || c == '-' && i + 1 < len && char.IsDigit(code[i + 1]))
                {
                    int end = i;
                    if (code[end] == '-') end++;
                    while (end < len && (char.IsDigit(code[end]) || code[end] == '.')) end++;
                    result.Append($"<color={SynNumber}>{EscapeRichText(code.Substring(i, end - i))}</color>");
                    i = end;
                    continue;
                }

                if (i + 4 <= len && code.Substring(i, 4) == "true")
                {
                    result.Append($"<color={SynKeyword}>true</color>"); i += 4; continue;
                }
                if (i + 5 <= len && code.Substring(i, 5) == "false")
                {
                    result.Append($"<color={SynKeyword}>false</color>"); i += 5; continue;
                }
                if (i + 4 <= len && code.Substring(i, 4) == "null")
                {
                    result.Append($"<color={SynKeyword}>null</color>"); i += 4; continue;
                }

                result.Append(EscapeRichText(c.ToString()));
                i++;
            }

            return result.ToString();
        }

        private string HighlightXml(string code)
        {
            // Simple XML/HTML highlighting
            string escaped = EscapeRichTextForCode(code);
            // Tags
            escaped = Regex.Replace(escaped, @"(&lt;/?)([\w:-]+)",
                $"<color={SynDefault}>$1</color><color={SynKeyword}>$2</color>");
            // Attribute names
            escaped = Regex.Replace(escaped, @"(\s)([\w:-]+)(=)",
                $"$1<color={SynType}>$2</color><color={SynDefault}>$3</color>");
            // Attribute values
            escaped = Regex.Replace(escaped, @"(&quot;[^&]*&quot;)",
                $"<color={SynString}>$1</color>");
            // Comments
            escaped = Regex.Replace(escaped, @"(&lt;!--.*?--&gt;)",
                $"<color={SynComment}>$1</color>");
            return escaped;
        }

        private string HighlightGeneric(string code)
        {
            // Generic: highlight strings and comments
            var result = new System.Text.StringBuilder();
            int i = 0;
            int len = code.Length;

            while (i < len)
            {
                char c = code[i];

                // # comment (bash, yaml, etc.)
                if (c == '#')
                {
                    int end = code.IndexOf('\n', i);
                    if (end < 0) end = len;
                    result.Append($"<color={SynComment}>");
                    result.Append(EscapeRichText(code.Substring(i, end - i)));
                    result.Append("</color>");
                    i = end;
                    continue;
                }

                // // comment
                if (c == '/' && i + 1 < len && code[i + 1] == '/')
                {
                    int end = code.IndexOf('\n', i);
                    if (end < 0) end = len;
                    result.Append($"<color={SynComment}>");
                    result.Append(EscapeRichText(code.Substring(i, end - i)));
                    result.Append("</color>");
                    i = end;
                    continue;
                }

                // String
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    int end = i + 1;
                    while (end < len && code[end] != quote)
                    {
                        if (code[end] == '\\') end++;
                        end++;
                    }
                    if (end < len) end++;
                    result.Append($"<color={SynString}>");
                    result.Append(EscapeRichText(code.Substring(i, end - i)));
                    result.Append("</color>");
                    i = end;
                    continue;
                }

                // Number
                if (char.IsDigit(c))
                {
                    int end = i;
                    while (end < len && (char.IsDigit(code[end]) || code[end] == '.')) end++;
                    result.Append($"<color={SynNumber}>");
                    result.Append(EscapeRichText(code.Substring(i, end - i)));
                    result.Append("</color>");
                    i = end;
                    continue;
                }

                if (c == '\n')
                {
                    result.Append('\n');
                    i++;
                    continue;
                }

                result.Append(EscapeRichText(c.ToString()));
                i++;
            }

            return result.ToString();
        }

        // ======================= INLINE FORMATTING =======================

        private string FormatInlineMarkdown(string text)
        {
            text = EscapeRichText(text);

            // Bold + italic
            text = Regex.Replace(text, @"\*\*\*(.+?)\*\*\*", "<b><i>$1</i></b>");
            // Bold
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>");
            text = Regex.Replace(text, @"__(.+?)__", "<b>$1</b>");
            // Italic
            text = Regex.Replace(text, @"(?<!\w)\*(.+?)\*(?!\w)", "<i>$1</i>");
            text = Regex.Replace(text, @"(?<!\w)_(.+?)_(?!\w)", "<i>$1</i>");
            // Inline code
            text = Regex.Replace(text, @"`(.+?)`", "<color=#E8A064><b>$1</b></color>");
            // Links [text](url) -> colored & underlined-looking text
            text = Regex.Replace(text, @"\[(.+?)\]\(([^)]+)\)",
                "<color=#5A9FD4><b>$1</b></color>");

            return text;
        }

        private string EscapeRichText(string text)
        {
            text = text.Replace("<", "\u2039").Replace(">", "\u203A");
            return text;
        }

        private string EscapeRichTextForCode(string text)
        {
            text = text.Replace("&", "&amp;");
            text = text.Replace("<", "&lt;").Replace(">", "&gt;");
            text = text.Replace("\"", "&quot;");
            return text;
        }

        // ======================= DRAWING HELPERS =======================

        private void DrawRichLabel(string text, GUIStyle style, float width)
        {
            var content = new GUIContent(text);
            float height = style.CalcHeight(content, width);
            EditorGUILayout.LabelField(text, style, GUILayout.Height(height));
        }

        private void DrawHeadingUnderline(float width, Color color, float thickness, float widthRatio)
        {
            Rect r = GUILayoutUtility.GetRect(width - 32, thickness);
            r.x += 16;
            r.width = (width - 48) * widthRatio;
            EditorGUI.DrawRect(r, color);
        }

        private void DrawHorizontalRule(float width)
        {
            GUILayout.Space(6);
            Rect r = GUILayoutUtility.GetRect(width - 32, 1);
            r.x += 16;
            r.width -= 32;
            EditorGUI.DrawRect(r, new Color(1, 1, 1, 0.15f));
            GUILayout.Space(6);
        }

        private void DrawAccentLine(float width)
        {
            Rect r = GUILayoutUtility.GetRect(width - 32, 2);
            r.x += 16;
            r.width = Mathf.Min(r.width - 32, 80);
            EditorGUI.DrawRect(r, AccentColor);
        }
    }
}
