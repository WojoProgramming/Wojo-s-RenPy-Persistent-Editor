using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WojoPersistentEditor.Views
{
    public partial class MessageDialog : Window
    {
        public MessageDialog()
        {
            InitializeComponent();
        }

        public MessageDialog(string header, string message)
            : this()
        {
            HeaderText.Text = header;
            MessageText.Text = message;
        }

        private void OkButton_Click(
            object? sender,
            RoutedEventArgs e
        )
        {
            Close();
        }
    }
}
