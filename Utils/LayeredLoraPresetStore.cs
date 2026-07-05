using CyanTools.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyanTools.Utils
{
    public sealed class LayeredLoraPresetStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _directory;

        public LayeredLoraPresetStore()
            : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs", "LayeredLoraMerge"))
        {
        }

        public LayeredLoraPresetStore(string directory)
        {
            _directory = string.IsNullOrWhiteSpace(directory)
                ? throw new ArgumentException("Preset directory cannot be empty.", nameof(directory))
                : directory;
        }

        public IReadOnlyList<string> LoadNames()
        {
            Directory.CreateDirectory(_directory);
            return Directory.GetFiles(_directory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }

        public LayeredLoraPresetConfig? Load(string name)
        {
            var cleanName = SanitizeFileName(name);
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                return null;
            }

            var path = GetPath(cleanName);
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<LayeredLoraPresetConfig>(json, SerializerOptions);
            if (config != null)
            {
                config.Name = cleanName;
                config.LayerWeights ??= Array.Empty<double>();
                config.AnimaBlockWeights ??= Array.Empty<LayeredLoraBlockPresetWeight>();
            }

            return config;
        }

        public void Save(LayeredLoraPresetConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var cleanName = SanitizeFileName(config.Name);
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                throw new ArgumentException("Preset name cannot be empty.", nameof(config));
            }

            Directory.CreateDirectory(_directory);
            config.Name = cleanName;
            config.LayerWeights = config.LayerWeights
                .Select(LayeredLoraWeightText.NormalizeSliderValue)
                .ToArray();
            config.AnimaBlockWeights = config.AnimaBlockWeights
                .Where(item => item.BlockIndex >= 0)
                .GroupBy(item => item.BlockIndex)
                .Select(group => new LayeredLoraBlockPresetWeight
                {
                    BlockIndex = group.Key,
                    Value = LayeredLoraWeightText.NormalizeSliderValue(group.Last().Value)
                })
                .OrderBy(item => item.BlockIndex)
                .ToArray();

            var json = JsonSerializer.Serialize(config, SerializerOptions);
            File.WriteAllText(GetPath(cleanName), json);
        }

        public void Delete(string name)
        {
            var cleanName = SanitizeFileName(name);
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                return;
            }

            var path = GetPath(cleanName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public bool Exists(string name)
        {
            var cleanName = SanitizeFileName(name);
            return !string.IsNullOrWhiteSpace(cleanName) && File.Exists(GetPath(cleanName));
        }

        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((name ?? string.Empty).Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        }

        private string GetPath(string name)
        {
            return Path.Combine(_directory, $"{name}.json");
        }
    }
}
