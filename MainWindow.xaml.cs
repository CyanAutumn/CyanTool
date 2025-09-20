using System.Diagnostics;
using System.Security.Policy;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Navigation;

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
        private ExportVideoFrameView _exportVideoFrameView;
        private ImageMetadataView _imageMetadataView;
        private GaussianBlurView _gaussianBlurView;
        private ImageClusteringView _imageClusteringView;
        private ConvertImageFormatView _convertImageFormatView;
        private TaggingView _taggingView;
        private TrainingSetBalancerView _trainingSetBalancerView;
        private ResolutionFilterView _resolutionFilterView;

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;

            _openMenuStoryboard = (Storyboard)FindResource("OpenMenuStoryboard");
            _closeMenuStoryboard = (Storyboard)FindResource("CloseMenuStoryboard");

            HomePageGrid.Visibility = Visibility.Visible;
            MainContent.Visibility = Visibility.Collapsed;
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
                HomePageGrid.Visibility = Visibility.Collapsed;
                MainContent.Visibility = Visibility.Visible;
                switch (selection)
                {
                    case "主页":
                        HomePageGrid.Visibility = Visibility.Visible;
                        MainContent.Visibility = Visibility.Collapsed;
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
                    case "分辨率筛选":
                        if (_resolutionFilterView == null) _resolutionFilterView = new ResolutionFilterView();
                        MainContent.Content = _resolutionFilterView;
                        break;
                    case "保留指定文件":
                        if (_keepFilesView == null) _keepFilesView = new KeepFilesView();
                        MainContent.Content = _keepFilesView;
                        break;
                    case "导出视频帧":
                        if (_exportVideoFrameView == null) _exportVideoFrameView = new ExportVideoFrameView();
                        MainContent.Content = _exportVideoFrameView;
                        break;
                    case "图片元数据":
                        if (_imageMetadataView == null) _imageMetadataView = new ImageMetadataView();
                        MainContent.Content = _imageMetadataView;
                        break;
                    case "高斯模糊":
                        if (_gaussianBlurView == null) _gaussianBlurView = new GaussianBlurView { DataContext = new ViewModel.GaussianBlurViewModel() };
                        MainContent.Content = _gaussianBlurView;
                        break;
                    case "图片聚类":
                        if (_imageClusteringView == null) _imageClusteringView = new ImageClusteringView { DataContext = new ViewModel.ImageClusteringViewModel() };
                        MainContent.Content = _imageClusteringView;
                        break;
                    case "转换图片格式":
                        if (_convertImageFormatView == null) _convertImageFormatView = new ConvertImageFormatView();
                        MainContent.Content = _convertImageFormatView;
                        break;
                    case "图片打标":
                        if (_taggingView == null) _taggingView = new TaggingView();
                        MainContent.Content = _taggingView;
                        break;
                    case "训练集配平":
                        if (_trainingSetBalancerView == null) _trainingSetBalancerView = new TrainingSetBalancerView();
                        MainContent.Content = _trainingSetBalancerView;
                        break;
                }
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true } );
            e.Handled = true;
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