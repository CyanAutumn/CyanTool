
using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("图片打标", 260)]
    public partial class TaggingView : UserControl
    {
        public TaggingView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.TaggingViewModel();
            }
        }
    }
}
