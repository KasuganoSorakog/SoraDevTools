using System.IO;
using MagicBox.Models;
using MagicBox.UI.Views;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicBox.UI.Components
{
    public static class FileIconGenerator
    {
        /// <summary>Large file icon for grid card thumbnails</summary>
        public static VisualElement CreateLarge(ResourceItem item)
        {
            return CreatePageIcon(item, 56, 68, 13, 11);
        }

        /// <summary>Small file icon for list view rows</summary>
        public static VisualElement CreateSmall(ResourceItem item)
        {
            var color = ContentGridView.GetTypeColor(item.Type);
            var ext = GetExtension(item.FileName, item.Type);

            var page = new VisualElement();
            page.style.width = 24;
            page.style.height = 26;
            page.style.backgroundColor = new Color(color.r, color.g, color.b, 0.15f);
            SetRadius(page, 3);
            SetBorder(page, 1, new Color(color.r, color.g, color.b, 0.3f));
            page.style.alignItems = Align.Center;
            page.style.justifyContent = Justify.Center;

            var label = new Label(ext);
            label.style.fontSize = 8;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            page.Add(label);

            return page;
        }

        /// <summary>Detail panel file icon</summary>
        public static VisualElement CreateDetail(ResourceItem item)
        {
            return CreatePageIcon(item, 56, 68, 14, 11);
        }

        private static VisualElement CreatePageIcon(ResourceItem item, float w, float h, float fontSize, float foldSize)
        {
            var color = ContentGridView.GetTypeColor(item.Type);
            var ext = GetExtension(item.FileName, item.Type);

            // Page body
            var page = new VisualElement();
            page.style.width = w;
            page.style.height = h;
            page.style.backgroundColor = new Color(color.r, color.g, color.b, 0.1f);
            page.style.borderTopLeftRadius = 4;
            page.style.borderTopRightRadius = foldSize;
            page.style.borderBottomLeftRadius = 4;
            page.style.borderBottomRightRadius = 4;
            SetBorder(page, 1, new Color(color.r, color.g, color.b, 0.3f));
            page.style.alignItems = Align.Center;
            page.style.justifyContent = Justify.Center;
            page.style.overflow = Overflow.Hidden;

            // Top color accent bar (stops before fold area)
            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.top = 0;
            bar.style.left = 0;
            bar.style.width = w - foldSize;
            bar.style.height = 4;
            bar.style.backgroundColor = color;
            page.Add(bar);

            // Fold corner indicator
            var fold = new VisualElement();
            fold.style.position = Position.Absolute;
            fold.style.top = 0;
            fold.style.right = 0;
            fold.style.width = foldSize;
            fold.style.height = foldSize;
            fold.style.backgroundColor = new Color(color.r, color.g, color.b, 0.2f);
            fold.style.borderBottomLeftRadius = foldSize * 0.6f;
            page.Add(fold);

            // Bottom color line
            var bottom = new VisualElement();
            bottom.style.position = Position.Absolute;
            bottom.style.bottom = 0;
            bottom.style.left = 0;
            bottom.style.width = Length.Percent(100);
            bottom.style.height = 3;
            bottom.style.backgroundColor = new Color(color.r, color.g, color.b, 0.4f);
            page.Add(bottom);

            // Extension text
            var label = new Label(ext);
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.letterSpacing = 1;
            page.Add(label);

            return page;
        }

        public static string GetExtension(string fileName, ResourceType type)
        {
            if (!string.IsNullOrEmpty(fileName))
            {
                var ext = Path.GetExtension(fileName);
                if (!string.IsNullOrEmpty(ext))
                {
                    ext = ext.TrimStart('.').ToUpper();
                    if (ext.Length > 5) ext = ext.Substring(0, 4);
                    return ext;
                }
            }
            return ContentGridView.GetTypeLabel(type);
        }

        private static void SetBorder(VisualElement el, float width, Color c)
        {
            el.style.borderTopWidth = width;
            el.style.borderBottomWidth = width;
            el.style.borderLeftWidth = width;
            el.style.borderRightWidth = width;
            el.style.borderTopColor = c;
            el.style.borderBottomColor = c;
            el.style.borderLeftColor = c;
            el.style.borderRightColor = c;
        }

        private static void SetRadius(VisualElement el, float r)
        {
            el.style.borderTopLeftRadius = r;
            el.style.borderTopRightRadius = r;
            el.style.borderBottomLeftRadius = r;
            el.style.borderBottomRightRadius = r;
        }
    }
}
