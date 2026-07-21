using System;
using System.ComponentModel;
using System.Windows;

namespace CyanTools.Utils
{
    public static class SystemMonitorWidgetController
    {
        private static readonly DesktopWidgetSettingsStore SettingsStore = new();
        private static DesktopWidgetSettings _settings = SettingsStore.Load();
        private static SystemMonitorWidgetWindow? _window;
        private static bool _isShuttingDown;

        public static bool IsEnabled => _settings.ShowSystemMonitorWidget;

        public static event Action<bool>? EnabledChanged;

        public static void RestoreFromSettings()
        {
            if (_settings.ShowSystemMonitorWidget)
            {
                ShowWindow();
            }
        }

        public static void SetEnabled(bool enabled)
        {
            if (!TryRunOnUiThread(() => SetEnabled(enabled)))
            {
                return;
            }

            bool changed = _settings.ShowSystemMonitorWidget != enabled;
            _settings.ShowSystemMonitorWidget = enabled;
            TrySaveSettings();

            if (enabled)
            {
                ShowWindow();
            }
            else
            {
                CloseWindow();
            }

            if (changed)
            {
                EnabledChanged?.Invoke(enabled);
            }
        }

        public static void SavePosition(double left, double top)
        {
            if (!double.IsFinite(left) || !double.IsFinite(top))
            {
                return;
            }

            _settings.SystemMonitorLeft = left;
            _settings.SystemMonitorTop = top;
            TrySaveSettings();
        }

        public static void PrepareForApplicationShutdown()
            => _isShuttingDown = true;

        public static void Shutdown()
        {
            PrepareForApplicationShutdown();
            if (_window != null)
            {
                SavePosition(_window.Left, _window.Top);
                SystemMonitorWidgetWindow closingWindow = _window;
                _window = null;
                closingWindow.Close();
            }
        }

        private static void ShowWindow()
        {
            if (_window != null)
            {
                if (!_window.IsVisible)
                {
                    _window.Show();
                }
                return;
            }

            var window = new SystemMonitorWidgetWindow();
            RestorePosition(window);
            window.Closing += Window_Closing;
            window.Closed += Window_Closed;
            _window = window;
            window.Show();
        }

        private static void CloseWindow()
        {
            if (_window == null)
            {
                return;
            }

            SavePosition(_window.Left, _window.Top);
            SystemMonitorWidgetWindow closingWindow = _window;
            _window = null;
            closingWindow.Close();
        }

        private static void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (sender is Window window)
            {
                SavePosition(window.Left, window.Top);
            }

            if (_isShuttingDown || !_settings.ShowSystemMonitorWidget)
            {
                return;
            }

            _settings.ShowSystemMonitorWidget = false;
            TrySaveSettings();
            EnabledChanged?.Invoke(false);
        }

        private static void Window_Closed(object? sender, EventArgs e)
        {
            if (ReferenceEquals(_window, sender))
            {
                _window = null;
            }
        }

        private static void RestorePosition(Window window)
        {
            double defaultLeft = SystemParameters.WorkArea.Right - window.Width - 16;
            double defaultTop = SystemParameters.WorkArea.Top + 16;
            double left = _settings.SystemMonitorLeft ?? defaultLeft;
            double top = _settings.SystemMonitorTop ?? defaultTop;

            bool intersectsVirtualDesktop =
                left + window.Width >= SystemParameters.VirtualScreenLeft &&
                left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                top + window.Height >= SystemParameters.VirtualScreenTop &&
                top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = intersectsVirtualDesktop ? left : defaultLeft;
            window.Top = intersectsVirtualDesktop ? top : defaultTop;
        }

        private static void TrySaveSettings()
        {
            try
            {
                SettingsStore.Save(_settings);
            }
            catch
            {

            }
        }

        private static bool TryRunOnUiThread(Action retry)
        {
            if (Application.Current?.Dispatcher is not { } dispatcher || dispatcher.CheckAccess())
            {
                return true;
            }

            dispatcher.Invoke(retry);
            return false;
        }
    }
}
