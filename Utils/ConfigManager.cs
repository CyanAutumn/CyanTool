
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CyanTools.Model;

namespace CyanTools.Utils
{
    public class ConfigManager
    {
        private static readonly string ConfigDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs");

        public ConfigManager()
        {
            Directory.CreateDirectory(ConfigDirectory);
        }

        public List<ConfigurationModel> LoadAllConfigurations()
        {
            var configs = new List<ConfigurationModel>();
            var files = Directory.GetFiles(ConfigDirectory, "*.json");

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var config = JsonSerializer.Deserialize<ConfigurationModel>(json);
                    if (config != null)
                    {
                        configs.Add(config);
                    }
                }
                catch (Exception ex)
                {

                    Console.WriteLine($"Error loading configuration file {file}: {ex.Message}");
                }
            }
            return configs.OrderBy(c => c.Name).ToList();
        }

        public void SaveConfiguration(ConfigurationModel config)
        {
            if (string.IsNullOrWhiteSpace(config.Name))
            {
                throw new ArgumentException("Configuration name cannot be empty.");
            }


            var fileName = new string(config.Name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray()) + ".json";
            var filePath = Path.Combine(ConfigDirectory, fileName);

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        public void DeleteConfiguration(string configName)
        { 
            if (string.IsNullOrWhiteSpace(configName))
            {
                return;
            }

            var fileName = new string(configName.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray()) + ".json";
            var filePath = Path.Combine(ConfigDirectory, fileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
