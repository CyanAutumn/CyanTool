using System.Windows;
using System.Windows.Controls;

namespace CyanTools.Utils
{
    public static class PasswordBoxHelper
    {
        public static readonly DependencyProperty BindPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BindPassword",
                typeof(bool),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(false, OnBindPasswordChanged));

        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BoundPassword",
                typeof(string),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(string.Empty, OnBoundPasswordChanged));

        private static readonly DependencyProperty IsUpdatingProperty =
            DependencyProperty.RegisterAttached(
                "IsUpdating",
                typeof(bool),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(false));

        public static bool GetBindPassword(DependencyObject obj) => (bool)obj.GetValue(BindPasswordProperty);

        public static void SetBindPassword(DependencyObject obj, bool value) => obj.SetValue(BindPasswordProperty, value);

        public static string? GetBoundPassword(DependencyObject obj) => (string?)obj.GetValue(BoundPasswordProperty);

        public static void SetBoundPassword(DependencyObject obj, string? value) => obj.SetValue(BoundPasswordProperty, value);

        private static bool GetIsUpdating(DependencyObject obj) => (bool)obj.GetValue(IsUpdatingProperty);

        private static void SetIsUpdating(DependencyObject obj, bool value) => obj.SetValue(IsUpdatingProperty, value);

        private static void OnBindPasswordChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is not PasswordBox box)
            {
                return;
            }

            if ((bool)e.OldValue)
            {
                box.PasswordChanged -= HandlePasswordChanged;
            }

            if ((bool)e.NewValue)
            {
                box.PasswordChanged += HandlePasswordChanged;
                box.Password = GetBoundPassword(box) ?? string.Empty;
            }
        }

        private static void OnBoundPasswordChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is not PasswordBox box || !GetBindPassword(box))
            {
                return;
            }

            box.PasswordChanged -= HandlePasswordChanged;

            if (!GetIsUpdating(box))
            {
                box.Password = e.NewValue as string ?? string.Empty;
            }

            box.PasswordChanged += HandlePasswordChanged;
        }

        private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not PasswordBox box)
            {
                return;
            }

            SetIsUpdating(box, true);
            SetBoundPassword(box, box.Password);
            SetIsUpdating(box, false);
        }
    }
}
