using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicBox.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicBox.UI.Views
{
    public class WelcomeView
    {
        public event Action OnCreateLibrary;
        public event Action OnOpenLibrary;
        public event Action<string> OnOpenRecent;

        public VisualElement Build(List<string> recentPaths)
        {
            var root = new VisualElement();
            root.AddToClassList("mb-welcome");

            var title = new Label(Loc.T("welcome.title"));
            title.AddToClassList("mb-welcome-title");
            root.Add(title);

            var sub = new Label(Loc.T("welcome.subtitle"));
            sub.AddToClassList("mb-welcome-sub");
            root.Add(sub);

            var createBtn = new Button(() => OnCreateLibrary?.Invoke()) { text = Loc.T("welcome.create") };
            createBtn.AddToClassList("mb-welcome-btn");
            createBtn.AddToClassList("mb-btn-primary");
            root.Add(createBtn);

            var openBtn = new Button(() => OnOpenLibrary?.Invoke()) { text = Loc.T("welcome.open") };
            openBtn.AddToClassList("mb-welcome-btn");
            openBtn.AddToClassList("mb-btn");
            root.Add(openBtn);

            if (recentPaths != null && recentPaths.Count > 0)
            {
                var label = new Label(Loc.T("welcome.recent"));
                label.style.marginTop = 24;
                label.style.fontSize = 12;
                label.style.color = new Color(0.48f, 0.55f, 0.63f);
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                root.Add(label);

                foreach (var path in recentPaths.Take(5))
                {
                    if (!Directory.Exists(path)) continue;
                    string name = Path.GetFileName(path);
                    var btn = new Button(() => OnOpenRecent?.Invoke(path)) { text = $"{name}  -  {path}" };
                    btn.AddToClassList("mb-recent-item");
                    btn.AddToClassList("mb-btn");
                    root.Add(btn);
                }
            }

            return root;
        }
    }
}
