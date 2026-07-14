using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace WojoPersistentEditor.Views
{
    public partial class PersistentLoaderView : UserControl
    {
        public event EventHandler? LoadFileRequested;

        public PersistentLoaderView()
        {
            InitializeComponent();
        }

        public void SetLoading(bool isLoading)
        {
            FileSelectorButton.IsEnabled = !isLoading;
            LoadingBar.IsVisible = isLoading;

            if (isLoading)
            {
                ShowStatus(
                    "Loading and decoding the persistent file...",
                    false
                );
            }
        }

        public void ShowStatus(string message, bool isError)
        {
            StatusText.Text = message;
            StatusText.Foreground = new SolidColorBrush(
                Color.Parse(isError ? "#FF8A8A" : "#B9B7C8")
            );
        }

        private void FileSelectorButton_Click(
            object? sender,
            RoutedEventArgs e
        )
        {
            LoadFileRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
