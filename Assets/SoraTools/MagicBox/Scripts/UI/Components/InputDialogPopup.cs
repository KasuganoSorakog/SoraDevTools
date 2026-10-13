using System;
using MagicBox.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicBox.UI.Components
{
    public class InputDialogPopup
    {
        private VisualElement _overlay;

        public void Show(VisualElement root, string title, string message, string defaultValue, Action<string> onConfirm)
        {
            Hide(root);

            _overlay = new VisualElement();
            _overlay.AddToClassList("mb-dialog-overlay");

            var dialog = new VisualElement();
            dialog.AddToClassList("mb-dialog");
            _overlay.Add(dialog);

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("mb-dialog-title");
            dialog.Add(titleLabel);

            if (!string.IsNullOrEmpty(message))
            {
                var msgLabel = new Label(message);
                msgLabel.AddToClassList("mb-dialog-msg");
                dialog.Add(msgLabel);
            }

            var inputField = new TextField();
            inputField.AddToClassList("mb-dialog-input");
            inputField.value = defaultValue ?? "";
            dialog.Add(inputField);

            var buttons = new VisualElement();
            buttons.AddToClassList("mb-dialog-buttons");
            dialog.Add(buttons);

            var cancelBtn = new Button(() => Hide(root)) { text = Loc.T("dialog.cancel") };
            cancelBtn.AddToClassList("mb-btn");
            buttons.Add(cancelBtn);

            var okBtn = new Button(() =>
            {
                string val = inputField.value;
                Hide(root);
                if (!string.IsNullOrEmpty(val)) onConfirm?.Invoke(val);
            }) { text = Loc.T("dialog.ok") };
            okBtn.AddToClassList("mb-btn");
            okBtn.AddToClassList("mb-btn-primary");
            buttons.Add(okBtn);

            root.Add(_overlay);

            // Focus input field
            inputField.schedule.Execute(() => inputField.Focus()).StartingIn(50);
        }

        public void Show(VisualElement root, string title, string message, Action onConfirm, Action onCancel = null)
        {
            Hide(root);

            _overlay = new VisualElement();
            _overlay.AddToClassList("mb-dialog-overlay");

            var dialog = new VisualElement();
            dialog.AddToClassList("mb-dialog");
            _overlay.Add(dialog);

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("mb-dialog-title");
            dialog.Add(titleLabel);

            var msgLabel = new Label(message);
            msgLabel.AddToClassList("mb-dialog-msg");
            msgLabel.style.whiteSpace = WhiteSpace.Normal;
            dialog.Add(msgLabel);

            var buttons = new VisualElement();
            buttons.AddToClassList("mb-dialog-buttons");
            dialog.Add(buttons);

            var cancelBtn = new Button(() => { Hide(root); onCancel?.Invoke(); }) { text = Loc.T("dialog.cancel") };
            cancelBtn.AddToClassList("mb-btn");
            buttons.Add(cancelBtn);

            var okBtn = new Button(() => { Hide(root); onConfirm?.Invoke(); }) { text = Loc.T("dialog.confirm") };
            okBtn.AddToClassList("mb-btn");
            okBtn.AddToClassList("mb-btn-primary");
            buttons.Add(okBtn);

            root.Add(_overlay);
        }

        public void Hide(VisualElement root)
        {
            if (_overlay != null && _overlay.parent == root)
                root.Remove(_overlay);
            _overlay = null;
        }
    }
}
