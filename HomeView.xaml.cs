using System;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace CyanTools
{
    public partial class HomeView : UserControl
    {
        public event EventHandler? SecretImageClicked;

        public HomeView()
        {
            InitializeComponent();
        }

        public void ShowUnlockedImage()
        {
            try
            {
                SecretImage.Source = new BitmapImage(new Uri("/da715a4e-51a3-4430-833b-a4e5f2a804a9.png", UriKind.Relative));
            }
            catch
            {

            }
        }

        private void SecretImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                SecretImageClicked?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
