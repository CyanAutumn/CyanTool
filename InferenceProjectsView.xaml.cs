using CyanTools.Navigation;
using CyanTools.ViewModel;
using System;
using System.Windows;
using System.Windows.Controls;

namespace CyanTools
{
    [ToolPage("跑图工程", 890)]
    public partial class InferenceProjectsView : UserControl
    {
        private readonly InferenceProjectsViewModel _viewModel;

        public InferenceProjectsView()
        {
            InitializeComponent();
            _viewModel = new InferenceProjectsViewModel();
            DataContext = _viewModel;
            Loaded += InferenceProjectsView_Loaded;
            if (Application.Current != null)
            {
                Application.Current.Exit += Application_Exit;
            }
        }

        private void InferenceProjectsView_Loaded(object sender, RoutedEventArgs e)
            => _viewModel.ReloadProjects();

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                textBox.ScrollToEnd();
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            _viewModel.Dispose();
            if (Application.Current != null)
            {
                Application.Current.Exit -= Application_Exit;
            }
        }
    }
}
