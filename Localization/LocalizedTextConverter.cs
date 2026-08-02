using System;
using System.Globalization;
using System.Windows.Data;

namespace CyanTools.Localization
{
    public sealed class LocalizedTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is string format && !string.IsNullOrWhiteSpace(format))
            {
                object formattedValue = value is string sourceText
                    ? LocalizationManager.Translate(sourceText)
                    : value;
                return LocalizationManager.Format(format, formattedValue);
            }

            return value is string text ? LocalizationManager.Translate(text) : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
