using CyanTools.Localization;
using CyanTools.Utils;
using CyanTools.ViewModel;
using System;
using System.Windows;
using System.Windows.Input;

namespace CyanTools
{
    public partial class SystemMonitorWidgetWindow : Window
    {
        private readonly SystemMonitorWidgetViewModel _viewModel = new();

        public SystemMonitorWidgetWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
            Loaded += Window_Loaded;
            Closed += Window_Closed;
            LocalizationManager.LanguageChanged += LocalizationManager_LanguageChanged;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.StartMonitoring();
            LocalizationManager.ApplyTo(this);
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _viewModel.StopMonitoring();
            LocalizationManager.LanguageChanged -= LocalizationManager_LanguageChanged;
        }

        private void LocalizationManager_LanguageChanged(object? sender, EventArgs e)
            => LocalizationManager.ApplyTo(this);

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            DragMove();
            SystemMonitorWidgetController.SavePosition(Left, Top);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => SystemMonitorWidgetController.SetEnabled(false);
    }
}
