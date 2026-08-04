using System.Windows;
using CyanTools.ViewModel;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace CyanTools
{
    public partial class AddTaggerWindow : FluentWindow
    {
        public TaggerModel ResultModel { get; private set; }

        public AddTaggerWindow()
        {
            InitializeComponent();
            var viewModel = new AddTaggerViewModel();
            viewModel.RequestClose += OnRequestClose;
            DataContext = viewModel;
        }

        private void OnRequestClose(bool dialogResult)
        {
            if (dialogResult && DataContext is AddTaggerViewModel viewModel)
            {
                ResultModel = viewModel.BuildTaggerModel();
            }

            DialogResult = dialogResult;
            Close();
        }
    }
}
