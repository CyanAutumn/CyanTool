using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace CyanTools
{
    public partial class MainWindow : Window
    {
        private bool _isMenuOpen = false;
        private readonly Storyboard _openMenuStoryboard;
        private readonly Storyboard _closeMenuStoryboard;
        private DanbooruCrawlerView _danbooruCrawlerView;
        private RealEsrganView _realEsrganView;
        private ToolLLM _toolLLMView;
        private ImageProcessingView _imageProcessingView;
        private DatasetManagerView _datasetManagerView;
        private PromptManagerView _promptManagerView;
        private ImageBucketingView _imageBucketingView;
        private ThreeStageView _threeStageView;
        private KeepFilesView _keepFilesView;

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;

            _openMenuStoryboard = (Storyboard)FindResource("OpenMenuStoryboard");
            _closeMenuStoryboard = (Storyboard)FindResource("CloseMenuStoryboard");

            MainContent.Content = new TextBlock
            {
                Text = "欢迎使用 Cyan Tools",
                FontSize = 24,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
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
            if (MenuListView.SelectedItem is System.Windows.Controls.ListViewItem selectedItem && selectedItem.Content is TextBlock textBlock)
            {
                string selection = textBlock.Text;
                switch (selection)
                {
                    case "主页":
                        MainContent.Content = new TextBlock { Text = "主页内容", FontSize = 18, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                        break;
                    case "LLM分类":
                        if (_toolLLMView == null) _toolLLMView = new ToolLLM();
                        MainContent.Content = _toolLLMView;
                        break;
                    case "Danbooru下载器":
                        if (_danbooruCrawlerView == null) _danbooruCrawlerView = new DanbooruCrawlerView();
                        MainContent.Content = _danbooruCrawlerView;
                        break;
                    case "图片超分":
                        if (_realEsrganView == null) _realEsrganView = new RealEsrganView();
                        MainContent.Content = _realEsrganView;
                        break;
                    case "图片处理":
                        if (_imageProcessingView == null) _imageProcessingView = new ImageProcessingView();
                        MainContent.Content = _imageProcessingView;
                        break;
                    case "数据集管理器":
                        if (_datasetManagerView == null) _datasetManagerView = new DatasetManagerView();
                        MainContent.Content = _datasetManagerView;
                        break;
                    case "Prompt管理器":
                        if (_promptManagerView == null) _promptManagerView = new PromptManagerView();
                        MainContent.Content = _promptManagerView;
                        break;
                    case "图片分辨率归类":
                        if (_imageBucketingView == null) _imageBucketingView = new ImageBucketingView();
                        MainContent.Content = _imageBucketingView;
                        break;
                    case "图片三分法":
                        if (_threeStageView == null) _threeStageView = new ThreeStageView();
                        MainContent.Content = _threeStageView;
                        break;
                    case "保留指定文件":
                        if (_keepFilesView == null) _keepFilesView = new KeepFilesView();
                        MainContent.Content = _keepFilesView;
                        break;
                    
                }
            }
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
            if (_danbooruCrawlerView != null && _danbooruCrawlerView.DataContext is CyanTools.ViewModel.DanbooruCrawlerViewModel viewModel)
            {
                await viewModel.SaveCookiesAsync();
            }
        }
    }
}