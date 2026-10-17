using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("格式转换", 250)]
    public partial class ConvertImageFormatView : UserControl
    {
        private System.Action? _pendingLibraryNavigation;

        public void OpenFromLibrary(string path, System.Action returnToLibrary, string? materialRoot = null)
        {
            if (DataContext is ViewModel.ConvertImageFormatViewModel { IsConverting: true })
                throw new System.InvalidOperationException("正在转换，请等待当前任务完成。");
            void Apply() => ((ViewModel.ConvertImageFormatViewModel)DataContext).EnterFromLibrary(path, returnToLibrary, materialRoot);
            if (IsLoaded) Apply();
            else _pendingLibraryNavigation = Apply;
        }
        public ConvertImageFormatView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += (_, _) => (DataContext as ViewModel.ConvertImageFormatViewModel)?.ClearLibraryNavigation();
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.ConvertImageFormatViewModel();
            }

            var pending = _pendingLibraryNavigation;
            _pendingLibraryNavigation = null;
            pending?.Invoke();
        }
    }
}
