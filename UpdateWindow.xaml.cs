using System.Windows;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace CyanTools
{
    public partial class UpdateWindow : FluentWindow
    {
        public UpdateWindow(string currentVersion, string installedVersion, string notes, bool isMandatory)
        {
            InitializeComponent();

            TitleText.Text = "Update Available";
            VersionText.Text = $"New version: {currentVersion} (Installed: {installedVersion})";
            NotesTextBox.Text = string.IsNullOrWhiteSpace(notes) ? "No release notes provided." : notes;

            if (isMandatory)
            {
                LaterButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
