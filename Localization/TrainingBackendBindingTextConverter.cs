using CyanTools.Model;
using System;
using System.Globalization;
using System.Windows.Data;

namespace CyanTools.Localization
{
    public sealed class TrainingBackendBindingTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not TrainingBackendBinding binding)
            {
                return string.Empty;
            }

            return parameter as string switch
            {
                "LocationSummary" => LocalizationManager.Format(
                    "根目录：{0}\n{1}：{2}",
                    binding.TrainerPath,
                    LocalizationManager.Translate(binding.ConfigurationDirectoryLabel),
                    binding.ConfigurationDirectory),
                _ => $"{(binding.IsManaged ? LocalizationManager.Translate(binding.Name) : binding.Name)} · {binding.BackendId}"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
