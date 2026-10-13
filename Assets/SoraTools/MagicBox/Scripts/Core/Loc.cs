using System;
using System.Collections.Generic;

namespace MagicBox.Core
{
    public enum Lang { En, Zh }

    public static class Loc
    {
        public static Lang Current { get; private set; } = Lang.En;
        public static event Action OnLanguageChanged;

        public static void Init()
        {
            var saved = AppSettings.Instance.Language;
            Current = saved == "zh" ? Lang.Zh : Lang.En;
        }

        public static void SetLanguage(Lang lang)
        {
            if (Current == lang) return;
            Current = lang;
            AppSettings.Instance.Language = lang == Lang.Zh ? "zh" : "en";
            AppSettings.Instance.Save();
            OnLanguageChanged?.Invoke();
        }

        public static string T(string key)
        {
            var dict = Current == Lang.Zh ? Zh : En;
            return dict.TryGetValue(key, out var val) ? val : key;
        }

        public static string T(string key, params object[] args)
        {
            var dict = Current == Lang.Zh ? Zh : En;
            if (!dict.TryGetValue(key, out var fmt)) return key;
            return string.Format(fmt, args);
        }

        // ── English ──────────────────────────────────────────
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            // Toolbar
            { "toolbar.import", "+ Import" },
            { "toolbar.scan_folder", "Scan Folder" },
            { "toolbar.sort", "Sort" },
            { "toolbar.grid", "Grid" },
            { "toolbar.list", "List" },
            { "toolbar.sort.date_newest", "Import Date (Newest)" },
            { "toolbar.sort.date_oldest", "Import Date (Oldest)" },
            { "toolbar.sort.name_az", "Name (A-Z)" },
            { "toolbar.sort.name_za", "Name (Z-A)" },
            { "toolbar.sort.size_largest", "Size (Largest)" },
            { "toolbar.sort.size_smallest", "Size (Smallest)" },
            { "toolbar.sort.type", "Type" },
            { "toolbar.sort.rating", "Rating" },
            { "toolbar.lang", "中" },

            // Welcome
            { "welcome.title", "MagicBox" },
            { "welcome.subtitle", "Development Resource Manager" },
            { "welcome.create", "Create New Library" },
            { "welcome.open", "Open Existing Library" },
            { "welcome.recent", "Recent Libraries" },

            // Sidebar
            { "sidebar.folders", "FOLDERS" },
            { "sidebar.tags", "TAGS" },
            { "sidebar.all", "All Resources" },
            { "sidebar.uncategorized", "Uncategorized" },
            { "sidebar.new_folder", "New Folder" },
            { "sidebar.new_folder_prompt", "Folder name:" },
            { "sidebar.new_folder_default", "New Folder" },
            { "sidebar.new_subfolder", "New Subfolder" },
            { "sidebar.new_subfolder_title", "New Subfolder" },
            { "sidebar.subfolder_prompt", "Name:" },
            { "sidebar.subfolder_default", "New Folder" },
            { "sidebar.rename", "Rename" },
            { "sidebar.rename_title", "Rename" },
            { "sidebar.rename_prompt", "New name:" },
            { "sidebar.color_prefix", "Color: {0}" },
            { "sidebar.delete", "Delete" },
            { "sidebar.delete_folder_title", "Delete Folder" },
            { "sidebar.delete_folder_msg", "Delete '{0}'?" },
            { "sidebar.new_tag", "New Tag" },
            { "sidebar.new_tag_prompt", "Tag name:" },
            { "sidebar.rename_tag", "Rename Tag" },
            { "sidebar.rename_tag_prompt", "New name:" },
            { "sidebar.delete_tag", "Delete Tag" },
            { "sidebar.delete_tag_title", "Delete Tag" },
            { "sidebar.delete_tag_msg", "Delete tag '{0}'?" },
            { "sidebar.create_subfolder", "Create Subfolder" },

            // Content
            { "content.all", "All Resources" },
            { "content.items", "{0} items" },
            { "content.search", "Search: \"{0}\"" },
            { "content.tag_prefix", "Tag: {0}" },
            { "content.empty", "No resources found\n\nDrag files here or click '+ Import'" },

            // Content list headers
            { "list.name", "Name" },
            { "list.type", "Type" },
            { "list.size", "Size" },
            { "list.date", "Date" },
            { "list.rating", "Rating" },

            // Detail panel
            { "detail.select", "Select a resource" },
            { "detail.info", "Info" },
            { "detail.size", "Size" },
            { "detail.date", "Date" },
            { "detail.path", "Path" },
            { "detail.metadata", "Metadata" },
            { "detail.version", "Version" },
            { "detail.source_url", "Source URL" },
            { "detail.tags", "Tags" },
            { "detail.add_tag", "+ Add Tag" },
            { "detail.create_tag", "+ Create New Tag" },
            { "detail.new_tag_title", "New Tag" },
            { "detail.new_tag_prompt", "Tag name:" },
            { "detail.notes", "Notes" },
            { "detail.cover", "Cover" },
            { "detail.cover_hint", "Click to select cover image" },
            { "detail.cover_select", "Select Image" },
            { "detail.cover_clear", "Clear" },
            { "detail.cover_dialog", "Select Cover Image" },
            { "detail.cover_filter", "Image Files\0*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.gif\0All Files\0*.*\0" },
            { "detail.open_file", "Open File" },
            { "detail.open_folder", "Open Folder" },
            { "detail.copy_to", "Copy to..." },

            // Context menu
            { "ctx.open_file", "Open File" },
            { "ctx.open_folder", "Open Folder" },
            { "ctx.copy_to", "Copy to..." },
            { "ctx.import_unity", "Import to Unity Project" },
            { "ctx.delete", "Delete" },
            { "ctx.delete_title", "Delete Resource" },
            { "ctx.delete_msg", "Delete '{0}' from library?" },

            // Dialog buttons
            { "dialog.cancel", "Cancel" },
            { "dialog.ok", "OK" },
            { "dialog.confirm", "Confirm" },

            // Detail
            { "detail.import_unity", "Import to Unity" },

            // Progress
            { "progress.importing", "Importing {0}/{1}: {2}" },
            { "progress.done", "Done — {0} imported, {1} skipped, {2} failed" },

            // Batch operations
            { "batch.add_tag", "Add Tag..." },
            { "batch.move_folder", "Move to Folder..." },
            { "batch.delete", "Delete ({0})" },
            { "batch.delete_msg", "Delete {0} selected resources from library?" },

            // Drag
            { "drag.items", "Move {0} items" },

            // Win32 dialogs
            { "win32.choose_library", "Choose Library Location" },
            { "win32.open_library", "Open Library Folder" },
            { "win32.import_files", "Select Files to Import" },
            { "win32.import_filter", "All Files\0*.*\0Unity Package\0*.unitypackage\0Archive\0*.zip;*.rar;*.7z\0" },
            { "win32.scan_folder", "Select Folder to Scan" },
            { "win32.copy_to", "Copy to..." },
        };

        // ── 中文 ──────────────────────────────────────────
        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>
        {
            // Toolbar
            { "toolbar.import", "+ 导入" },
            { "toolbar.scan_folder", "扫描文件夹" },
            { "toolbar.sort", "排序" },
            { "toolbar.grid", "网格" },
            { "toolbar.list", "列表" },
            { "toolbar.sort.date_newest", "导入日期（最新）" },
            { "toolbar.sort.date_oldest", "导入日期（最早）" },
            { "toolbar.sort.name_az", "名称（A-Z）" },
            { "toolbar.sort.name_za", "名称（Z-A）" },
            { "toolbar.sort.size_largest", "大小（最大）" },
            { "toolbar.sort.size_smallest", "大小（最小）" },
            { "toolbar.sort.type", "类型" },
            { "toolbar.sort.rating", "评分" },
            { "toolbar.lang", "EN" },

            // Welcome
            { "welcome.title", "MagicBox" },
            { "welcome.subtitle", "开发资源管理器" },
            { "welcome.create", "创建新资源库" },
            { "welcome.open", "打开已有资源库" },
            { "welcome.recent", "最近的资源库" },

            // Sidebar
            { "sidebar.folders", "文件夹" },
            { "sidebar.tags", "标签" },
            { "sidebar.all", "所有资源" },
            { "sidebar.uncategorized", "未分类" },
            { "sidebar.new_folder", "新建文件夹" },
            { "sidebar.new_folder_prompt", "文件夹名称：" },
            { "sidebar.new_folder_default", "新文件夹" },
            { "sidebar.new_subfolder", "新建子文件夹" },
            { "sidebar.new_subfolder_title", "新建子文件夹" },
            { "sidebar.subfolder_prompt", "名称：" },
            { "sidebar.subfolder_default", "新文件夹" },
            { "sidebar.rename", "重命名" },
            { "sidebar.rename_title", "重命名" },
            { "sidebar.rename_prompt", "新名称：" },
            { "sidebar.color_prefix", "颜色：{0}" },
            { "sidebar.delete", "删除" },
            { "sidebar.delete_folder_title", "删除文件夹" },
            { "sidebar.delete_folder_msg", "删除 '{0}'？" },
            { "sidebar.new_tag", "新建标签" },
            { "sidebar.new_tag_prompt", "标签名称：" },
            { "sidebar.rename_tag", "重命名标签" },
            { "sidebar.rename_tag_prompt", "新名称：" },
            { "sidebar.delete_tag", "删除标签" },
            { "sidebar.delete_tag_title", "删除标签" },
            { "sidebar.delete_tag_msg", "删除标签 '{0}'？" },
            { "sidebar.create_subfolder", "新建子文件夹" },

            // Content
            { "content.all", "所有资源" },
            { "content.items", "{0} 项" },
            { "content.search", "搜索：\"{0}\"" },
            { "content.tag_prefix", "标签：{0}" },
            { "content.empty", "未找到资源\n\n将文件拖放到此处或点击"+ "导入" },

            // Content list headers
            { "list.name", "名称" },
            { "list.type", "类型" },
            { "list.size", "大小" },
            { "list.date", "日期" },
            { "list.rating", "评分" },

            // Detail panel
            { "detail.select", "选择一个资源" },
            { "detail.info", "信息" },
            { "detail.size", "大小" },
            { "detail.date", "日期" },
            { "detail.path", "路径" },
            { "detail.metadata", "元数据" },
            { "detail.version", "版本" },
            { "detail.source_url", "来源链接" },
            { "detail.tags", "标签" },
            { "detail.add_tag", "+ 添加标签" },
            { "detail.create_tag", "+ 新建标签" },
            { "detail.new_tag_title", "新建标签" },
            { "detail.new_tag_prompt", "标签名称：" },
            { "detail.notes", "备注" },
            { "detail.cover", "封面" },
            { "detail.cover_hint", "点击选择封面图片" },
            { "detail.cover_select", "选择图片" },
            { "detail.cover_clear", "清除" },
            { "detail.cover_dialog", "选择封面图片" },
            { "detail.cover_filter", "图片文件\0*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.gif\0所有文件\0*.*\0" },
            { "detail.open_file", "打开文件" },
            { "detail.open_folder", "打开文件夹" },
            { "detail.copy_to", "复制到..." },

            // Context menu
            { "ctx.open_file", "打开文件" },
            { "ctx.open_folder", "打开文件夹" },
            { "ctx.copy_to", "复制到..." },
            { "ctx.import_unity", "导入到 Unity 项目" },
            { "ctx.delete", "删除" },
            { "ctx.delete_title", "删除资源" },
            { "ctx.delete_msg", "从资源库中删除 '{0}'？" },

            // Dialog buttons
            { "dialog.cancel", "取消" },
            { "dialog.ok", "确定" },
            { "dialog.confirm", "确认" },

            // Detail
            { "detail.import_unity", "导入到 Unity" },

            // Progress
            { "progress.importing", "正在导入 {0}/{1}：{2}" },
            { "progress.done", "完成 — 已导入 {0}，跳过 {1}，失败 {2}" },

            // Batch operations
            { "batch.add_tag", "添加标签..." },
            { "batch.move_folder", "移动到文件夹..." },
            { "batch.delete", "删除（{0}）" },
            { "batch.delete_msg", "从资源库中删除 {0} 个选中的资源？" },

            // Drag
            { "drag.items", "移动 {0} 项" },

            // Win32 dialogs
            { "win32.choose_library", "选择资源库位置" },
            { "win32.open_library", "打开资源库文件夹" },
            { "win32.import_files", "选择要导入的文件" },
            { "win32.import_filter", "所有文件\0*.*\0Unity 包\0*.unitypackage\0压缩包\0*.zip;*.rar;*.7z\0" },
            { "win32.scan_folder", "选择要扫描的文件夹" },
            { "win32.copy_to", "复制到..." },
        };
    }
}
