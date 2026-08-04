using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfControlAppearance = Wpf.Ui.Controls.ControlAppearance;
using WpfMessageBox = Wpf.Ui.Controls.MessageBox;
using WpfMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using WpfSymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using WpfSymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace CyanTools.Localization
{
    public static class LocalizedMessageBox
    {
        public static MessageBoxResult Show(string messageBoxText)
            => ShowCore(null, messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);

        public static MessageBoxResult Show(string messageBoxText, string caption)
            => ShowCore(null, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
            => ShowCore(null, messageBoxText, caption, button, MessageBoxImage.None);

        public static MessageBoxResult Show(
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon)
            => ShowCore(null, messageBoxText, caption, button, icon);

        public static MessageBoxResult Show(
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult)
            => ShowCore(null, messageBoxText, caption, button, icon, defaultResult);

        public static MessageBoxResult Show(
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options)
            => options == MessageBoxOptions.None
                ? ShowCore(null, messageBoxText, caption, button, icon, defaultResult)
                : ShowNative(null, Localize(messageBoxText), Localize(caption), button, icon, defaultResult, options);

        public static MessageBoxResult Show(Window owner, string messageBoxText)
            => ShowCore(owner, messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption)
            => ShowCore(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

        public static MessageBoxResult Show(
            Window owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button)
            => ShowCore(owner, messageBoxText, caption, button, MessageBoxImage.None);

        public static MessageBoxResult Show(
            Window owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon)
            => ShowCore(owner, messageBoxText, caption, button, icon);

        public static MessageBoxResult Show(
            Window owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult)
            => ShowCore(owner, messageBoxText, caption, button, icon, defaultResult);

        public static MessageBoxResult Show(
            Window owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options)
            => options == MessageBoxOptions.None
                ? ShowCore(owner, messageBoxText, caption, button, icon, defaultResult)
                : ShowNative(owner, Localize(messageBoxText), Localize(caption), button, icon, defaultResult, options);

        private static MessageBoxResult ShowCore(
            Window? owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            string localizedMessage = Localize(messageBoxText);
            string localizedCaption = Localize(caption);
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                return dispatcher.Invoke(
                    () => ShowOnUiThread(owner, localizedMessage, localizedCaption, button, icon, defaultResult));
            }

            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                return ShowNative(
                    owner,
                    localizedMessage,
                    localizedCaption,
                    button,
                    icon,
                    defaultResult,
                    MessageBoxOptions.None);
            }

            return ShowOnUiThread(owner, localizedMessage, localizedCaption, button, icon, defaultResult);
        }

        private static MessageBoxResult ShowOnUiThread(
            Window? owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult)
        {
            MessageBoxResult primaryResult = GetPrimaryResult(button);
            if (defaultResult != MessageBoxResult.None && defaultResult != primaryResult)
            {
                return ShowNative(
                    owner,
                    messageBoxText,
                    caption,
                    button,
                    icon,
                    defaultResult,
                    MessageBoxOptions.None);
            }

            try
            {
                return ShowFluent(owner, messageBoxText, caption, button, icon);
            }
            catch (Exception)
            {

                return ShowNative(
                    owner,
                    messageBoxText,
                    caption,
                    button,
                    icon,
                    defaultResult,
                    MessageBoxOptions.None);
            }
        }

        private static MessageBoxResult ShowFluent(
            Window? owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon)
        {
            var dialog = new WpfMessageBox
            {
                Title = caption,
                ShowTitle = !string.IsNullOrWhiteSpace(caption),
                Content = CreateContent(messageBoxText, icon),
                Owner = owner,
                Topmost = owner == null,
                WindowStartupLocation = owner == null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                MinWidth = 320,
                MaxWidth = 560,
                PrimaryButtonAppearance = icon switch
                {
                    MessageBoxImage.Error => WpfControlAppearance.Danger,
                    MessageBoxImage.Warning => WpfControlAppearance.Caution,
                    _ => WpfControlAppearance.Primary
                }
            };

            ConfigureButtons(
                dialog,
                button,
                out MessageBoxResult primaryResult,
                out MessageBoxResult secondaryResult,
                out MessageBoxResult closeResult);

            WpfMessageBoxResult result = dialog.ShowDialogAsync().GetAwaiter().GetResult();
            return result switch
            {
                WpfMessageBoxResult.Primary => primaryResult,
                WpfMessageBoxResult.Secondary => secondaryResult,
                _ => closeResult
            };
        }

        private static void ConfigureButtons(
            WpfMessageBox dialog,
            MessageBoxButton button,
            out MessageBoxResult primaryResult,
            out MessageBoxResult secondaryResult,
            out MessageBoxResult closeResult)
        {
            dialog.PrimaryButtonText = Localize("确定");
            dialog.SecondaryButtonText = string.Empty;
            dialog.CloseButtonText = string.Empty;
            dialog.IsCloseButtonEnabled = false;

            primaryResult = MessageBoxResult.OK;
            secondaryResult = MessageBoxResult.None;
            closeResult = MessageBoxResult.OK;

            switch (button)
            {
                case MessageBoxButton.OKCancel:
                    dialog.CloseButtonText = Localize("取消");
                    dialog.IsCloseButtonEnabled = true;
                    closeResult = MessageBoxResult.Cancel;
                    break;

                case MessageBoxButton.YesNo:
                    dialog.PrimaryButtonText = Localize("是");
                    dialog.SecondaryButtonText = Localize("否");
                    primaryResult = MessageBoxResult.Yes;
                    secondaryResult = MessageBoxResult.No;
                    closeResult = MessageBoxResult.No;
                    break;

                case MessageBoxButton.YesNoCancel:
                    dialog.PrimaryButtonText = Localize("是");
                    dialog.SecondaryButtonText = Localize("否");
                    dialog.CloseButtonText = Localize("取消");
                    dialog.IsCloseButtonEnabled = true;
                    primaryResult = MessageBoxResult.Yes;
                    secondaryResult = MessageBoxResult.No;
                    closeResult = MessageBoxResult.Cancel;
                    break;
            }
        }

        private static object CreateContent(string messageBoxText, MessageBoxImage icon)
        {
            var text = new TextBlock
            {
                Text = messageBoxText,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 470,
                VerticalAlignment = VerticalAlignment.Center
            };

            (WpfSymbolRegular symbol, string brushKey)? iconDetails = icon switch
            {
                MessageBoxImage.Error => (WpfSymbolRegular.ErrorCircle24, "SystemFillColorCriticalBrush"),
                MessageBoxImage.Question => (WpfSymbolRegular.QuestionCircle24, "AccentTextFillColorPrimaryBrush"),
                MessageBoxImage.Warning => (WpfSymbolRegular.Warning24, "SystemFillColorCautionBrush"),
                MessageBoxImage.Information => (WpfSymbolRegular.Info24, "AccentTextFillColorPrimaryBrush"),
                _ => null
            };

            if (iconDetails == null)
            {
                return text;
            }

            var symbolIcon = new WpfSymbolIcon
            {
                Symbol = iconDetails.Value.symbol,
                FontSize = 26,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top
            };

            if (Application.Current?.TryFindResource(iconDetails.Value.brushKey) is Brush brush)
            {
                symbolIcon.Foreground = brush;
            }

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            panel.Children.Add(symbolIcon);
            panel.Children.Add(text);
            return panel;
        }

        private static MessageBoxResult GetPrimaryResult(MessageBoxButton button)
            => button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
                ? MessageBoxResult.Yes
                : MessageBoxResult.OK;

        private static MessageBoxResult ShowNative(
            Window? owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options)
            => owner == null
                ? System.Windows.MessageBox.Show(
                    messageBoxText,
                    caption,
                    button,
                    icon,
                    defaultResult,
                    options)
                : System.Windows.MessageBox.Show(
                    owner,
                    messageBoxText,
                    caption,
                    button,
                    icon,
                    defaultResult,
                    options);

        private static string Localize(string? value)
            => LocalizationManager.Translate(value);
    }
}
