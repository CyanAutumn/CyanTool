using System.Windows.Controls;

namespace CyanTools
{
    public partial class TrainingSetBalancerView : UserControl
    {
        public TrainingSetBalancerView()
        {
            InitializeComponent();
            DataContext = new TrainingSetBalancerViewModel();
        }
    }
}
