using System;
using System.Globalization;
using System.Windows.Data;

namespace CyanTools.Utils
{
    public class ReferenceEqualsMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
            {
                return false;
            }

            var left = values[0];
            var right = values[1];

            if (left == null || right == null)
            {
                return false;
            }

            return ReferenceEquals(left, right) || left.Equals(right);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return Array.Empty<object>();
        }
    }
}
