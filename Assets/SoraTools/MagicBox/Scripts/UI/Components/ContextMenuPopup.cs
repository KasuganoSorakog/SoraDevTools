using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicBox.UI.Components
{
    public class ContextMenuPopup
    {
        private VisualElement _overlay;
        private VisualElement _menu;

        public class MenuItem
        {
            public string Label;
            public Action Callback;
            public bool IsSeparator;
            public MenuItem(string label, Action cb) { Label = label; Callback = cb; }
            public static MenuItem Separator() => new MenuItem(null, null) { IsSeparator = true };
        }

        public void Show(VisualElement root, Vector2 position, List<MenuItem> items)
        {
            Hide(root);

            _overlay = new VisualElement();
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.top = 0;
            _overlay.style.right = 0; _overlay.style.bottom = 0;
            _overlay.RegisterCallback<ClickEvent>(evt => { Hide(root); evt.StopPropagation(); });
            root.Add(_overlay);

            _menu = new VisualElement();
            _menu.AddToClassList("mb-ctx-menu");
            _menu.style.left = position.x;
            _menu.style.top = position.y;

            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    var sep = new VisualElement();
                    sep.AddToClassList("mb-ctx-separator");
                    _menu.Add(sep);
                }
                else
                {
                    var btn = new Button(() => { item.Callback?.Invoke(); Hide(root); });
                    btn.text = item.Label;
                    btn.AddToClassList("mb-ctx-item");
                    _menu.Add(btn);
                }
            }

            _overlay.Add(_menu);

            // Clamp to screen bounds next frame
            _menu.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                var rootRect = root.worldBound;
                var menuRect = _menu.worldBound;
                float x = position.x, y = position.y;
                if (x + menuRect.width > rootRect.width) x = rootRect.width - menuRect.width - 4;
                if (y + menuRect.height > rootRect.height) y = rootRect.height - menuRect.height - 4;
                if (x < 0) x = 0; if (y < 0) y = 0;
                _menu.style.left = x; _menu.style.top = y;
            });
        }

        public void Hide(VisualElement root)
        {
            if (_overlay != null && _overlay.parent == root)
                root.Remove(_overlay);
            _overlay = null;
            _menu = null;
        }
    }
}
