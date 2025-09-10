using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CyanTools.Utils
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool booleanValue)
            {
                if (parameter != null && parameter.ToString() == "Inverse")
                {
                    return booleanValue ? Visibility.Collapsed : Visibility.Visible;
                }
                return booleanValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}