using System.Windows;

namespace CyanTools.Localization
{
    public static class LocalizedMessageBox
    {
        public static MessageBoxResult Show(string messageBoxText)
            => System.Windows.MessageBox.Show(Localize(messageBoxText));

        public static MessageBoxResult Show(string messageBoxText, string caption)
            => System.Windows.MessageBox.Show(Localize(messageBoxText), Localize(caption));

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
            => System.Windows.MessageBox.Show(Localize(messageBoxText), Localize(caption), button);

        public static MessageBoxResult Show(
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon)
            => System.Windows.MessageBox.Show(Localize(messageBoxText), Localize(caption), button, icon);

        public static MessageBoxResult Show(
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult)
            => System.Windows.MessageBox.Show(Localize(messageBoxText), Localize(caption), button, icon, defaultResult);

        public static MessageBoxResult Show(
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options)
            => System.Windows.MessageBox.Show(Localize(messageBoxText), Localize(caption), button, icon, defaultResult, options);

        public static MessageBoxResult Show(Window owner, string messageBoxText)
            => System.Windows.MessageBox.Show(owner, Localize(messageBoxText));

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption)
            => System.Windows.MessageBox.Show(owner, Localize(messageBoxText), Localize(caption));

        public static MessageBoxResult Show(
            Window owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button)
            => System.Windows.MessageBox.Show(owner, Localize(messageBoxText), Localize(caption), button);

        public static MessageBoxResult Show(
            Window owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon)
            => System.Windows.MessageBox.Show(owner, Localize(messageBoxText), Localize(caption), button, icon);

        public static MessageBoxResult Show(
            Window owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult)
            => System.Windows.MessageBox.Show(owner, Localize(messageBoxText), Localize(caption), button, icon, defaultResult);

        public static MessageBoxResult Show(
            Window owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options)
            => System.Windows.MessageBox.Show(owner, Localize(messageBoxText), Localize(caption), button, icon, defaultResult, options);

        private static string Localize(string? value)
            => LocalizationManager.Translate(value);
    }
}
