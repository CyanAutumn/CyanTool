using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using CyanTools.Navigation;
using CyanTools.Utils;

namespace CyanTools
{
    public partial class MainWindow : Window
    {
        private bool _isMenuOpen = false;
        private readonly Storyboard _openMenuStoryboard;
        private readonly Storyboard _closeMenuStoryboard;
        private readonly ToolNavigationService _navigationService = new();
        private readonly ObservableCollection<NavigationMenuEntry> _menuEntries = new();
        private bool _downloadersUnlocked;
        private int _secretImageClickCount;

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;

            _openMenuStoryboard = (Storyboard)FindResource("OpenMenuStoryboard");
            _closeMenuStoryboard = (Storyboard)FindResource("CloseMenuStoryboard");

            HomePageGrid.Visibility = Visibility.Visible;
            MainContent.Visibility = Visibility.Collapsed;

            MenuListView.ItemsSource = _menuEntries;
            MenuListView.DisplayMemberPath = nameof(NavigationMenuEntry.DisplayName);

            LoadPages();
        }

        private void MenuToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isMenuOpen)
            {
                _closeMenuStoryboard.Begin(this);
            }
            else
            {
                _openMenuStoryboard.Begin(this);
            }
            _isMenuOpen = !_isMenuOpen;
        }

        private void MenuListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MenuListView.SelectedItem is not NavigationMenuEntry entry)
            {
                return;
            }

            if (entry.IsHome)
            {
                ShowHome();
                return;
            }

            var page = entry.Metadata;
            if (page == null)
            {
                return;
            }

            if (page.RequiresUnlock && !_downloadersUnlocked)
            {
                return;
            }

            UserControl? control = null;
            try
            {
                control = _navigationService.GetOrCreateControl(page);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"页面加载失败: {page.DisplayName}\n{page.ControlType.FullName}\n\n{ex}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            if (control != null)
            {
                HomePageGrid.Visibility = Visibility.Collapsed;
                MainContent.Visibility = Visibility.Visible;
                MainContent.Content = control;
            }
        }

        private void SecretImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _downloadersUnlocked)
            {
                return;
            }

            _secretImageClickCount++;
            if (_secretImageClickCount >= 5)
            {
                _downloadersUnlocked = true;
                UnlockState.DownloadersUnlocked = true;
                RefreshMenuItems();
                try
                {
                    SecretImage.Source = new BitmapImage(new Uri("/da715a4e-51a3-4430-833b-a4e5f2a804a9.png", UriKind.Relative));
                }
                catch
                {

                }
                MenuListView.UpdateLayout();
                MessageBox.Show("再敲咬你啊", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true } );
            e.Handled = true;
        }

        private void LoadPages()
        {
            RefreshMenuItems();
        }

        private void RefreshMenuItems()
        {
            _menuEntries.Clear();
            foreach (var entry in _navigationService.GetMenuEntries(_downloadersUnlocked))
            {
                _menuEntries.Add(entry);
            }
        }

        private void ShowHome()
        {
            HomePageGrid.Visibility = Visibility.Visible;
            MainContent.Visibility = Visibility.Collapsed;
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                this.DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private async void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_navigationService.TryGetCachedControl(typeof(DanbooruCrawlerView), out var danbooruView) &&
                danbooruView.DataContext is ViewModel.DanbooruCrawlerViewModel viewModel)
            {
                await viewModel.SaveCookiesAsync();
            }
        }
    }
}
