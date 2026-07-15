using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WojoPersistentEditor.Models;

namespace WojoPersistentEditor.Controls
{
    public partial class VariableEditorControl : UserControl
    {
        private PersistentVariable? variable;
        private bool isPreviewMode;

        public VariableEditorControl()
        {
            InitializeComponent();
        }

        public void LoadVariable(
            PersistentVariable variableToLoad,
            int startIndex,
            int valueCount,
            bool previewMode
        )
        {
            variable = variableToLoad;
            isPreviewMode = previewMode;

            VariableNameLabel.Text =
                variable.Name + " (" + variable.Type + ")";
            ToolTip.SetTip(
                VariableNameLabel,
                VariableNameLabel.Text
            );
            ValuesPanel.Children.Clear();

            int safeStartIndex = Math.Max(0, startIndex);
            int endIndex = Math.Min(
                safeStartIndex + Math.Max(0, valueCount),
                variable.Values.Count
            );

            if (
                variable.IsCollection &&
                (
                    safeStartIndex > 0 ||
                    endIndex < variable.Values.Count
                )
            )
            {
                VariableNameLabel.Text +=
                    " [" + (safeStartIndex + 1) +
                    "-" + endIndex +
                    " / " + variable.Values.Count + "]";

                ToolTip.SetTip(
                    VariableNameLabel,
                    VariableNameLabel.Text
                );
            }

            for (int i = safeStartIndex; i < endIndex; i++)
            {
                PersistentEditorValue editorValue = variable.Values[i];
                ValuesPanel.Children.Add(CreateValueBox(editorValue));
            }
        }

        private TextBox CreateValueBox(
            PersistentEditorValue editorValue
        )
        {
            TextBox valueBox = new TextBox
            {
                Width = variable != null && variable.IsCollection
                    ? 160
                    : 690,
                Height = 35,
                Margin = new Thickness(4),
                Padding = new Thickness(8, 0),
                Foreground = new SolidColorBrush(
                    Color.Parse("#F5F5F5")
                ),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                FontSize = 14,
                Text = editorValue.Text,
                TextWrapping = TextWrapping.NoWrap,
                AcceptsReturn = false,
                IsReadOnly = isPreviewMode,
                IsTabStop = !isPreviewMode,
                Focusable = !isPreviewMode,
                Cursor = isPreviewMode
                    ? new Cursor(StandardCursorType.Arrow)
                    : null,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            if (isPreviewMode)
            {
                // Styl korzysta z tego samego zasobu co tło widoku, więc
                // zmiana koloru aplikacji automatycznie zmieni też Preview.
                // The style uses the same resource as the view background,
                // so changing the application color also updates Preview.
                valueBox.Classes.Add("preview-value");
            }
            else
            {
                valueBox.Background = new SolidColorBrush(
                    Color.Parse("#1B1924")
                );
            }

            ToolTip.SetTip(valueBox, editorValue.Text);

            valueBox.TextChanged += delegate
            {
                editorValue.Text = valueBox.Text ?? string.Empty;
                ToolTip.SetTip(valueBox, editorValue.Text);
            };

            return valueBox;
        }
    }
}
