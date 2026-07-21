using System;
using System.IO;
using System.Text.Json;

namespace CyanTools.Utils
{
    public sealed class DesktopWidgetSettings
    {
        public bool ShowSystemMonitorWidget { get; set; }
        public double? SystemMonitorLeft { get; set; }
        public double? SystemMonitorTop { get; set; }
    }

    public sealed class DesktopWidgetSettingsStore
    {
        private readonly string _settingsPath;

        public DesktopWidgetSettingsStore(string? settingsPath = null)
        {
            _settingsPath = settingsPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CyanTools",
                "desktop-widgets.json");
        }

        public DesktopWidgetSettings Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new DesktopWidgetSettings();
                }

                string json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<DesktopWidgetSettings>(json) ?? new DesktopWidgetSettings();
            }
            catch
            {
                return new DesktopWidgetSettings();
            }
        }

        public void Save(DesktopWidgetSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            string? directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = _settingsPath + ".tmp";
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
    }
}
