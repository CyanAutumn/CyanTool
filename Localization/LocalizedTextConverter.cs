using System;
using System.Globalization;
using System.Windows.Data;

namespace CyanTools.Localization
{
    public sealed class LocalizedTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is string text ? LocalizationManager.Translate(text) : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
