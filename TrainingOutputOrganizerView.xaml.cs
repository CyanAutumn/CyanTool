using System.Windows.Controls;
using CyanTools.ViewModel;

namespace CyanTools
{
    public partial class TrainingOutputOrganizerView : UserControl
    {
        public TrainingOutputOrganizerView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new TrainingOutputOrganizerViewModel();
            }
        }
    }
}
