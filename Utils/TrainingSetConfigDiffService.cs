using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CyanTools.Model;
using Newtonsoft.Json.Linq;

namespace CyanTools.Utils
{
    public static class TrainingSetConfigDiffService
    {
        public static IReadOnlyList<TrainingSetConfigDiffRow> Compare(
            string beforeFileName,
            string beforeContent,
            string afterFileName,
            string afterContent)
        {
            var before = FlattenConfig(beforeFileName, beforeContent);
            var after = FlattenConfig(afterFileName, afterContent);
            var keys = before.Keys
                .Concat(after.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase);

            var rows = new List<TrainingSetConfigDiffRow>();
            foreach (var key in keys)
            {
                var hasBefore = before.TryGetValue(key, out var oldValue);
                var hasAfter = after.TryGetValue(key, out var newValue);
                if (hasBefore && hasAfter && string.Equals(oldValue, newValue, StringComparison.Ordinal))
                {
                    continue;
                }

                rows.Add(new TrainingSetConfigDiffRow
                {
                    Key = key,
                    OldValue = oldValue ?? string.Empty,
                    NewValue = newValue ?? string.Empty,
                    Kind = hasBefore && hasAfter
                        ? TrainingSetConfigDiffKind.Modified
                        : hasBefore ? TrainingSetConfigDiffKind.Removed : TrainingSetConfigDiffKind.Added
                });
            }

            return rows;
        }

        private static Dictionary<string, string> FlattenConfig(string fileName, string content)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension == ".json")
            {
                return FlattenJson(content);
            }

            if (extension == ".toml")
            {
                return FlattenToml(content);
            }

            return FlattenLines(content);
        }

        private static Dictionary<string, string> FlattenJson(string content)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var token = JToken.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
            FlattenJsonToken(token, string.Empty, result);
            return result;
        }

        private static void FlattenJsonToken(JToken token, string prefix, IDictionary<string, string> result)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    var key = string.IsNullOrWhiteSpace(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    FlattenJsonToken(property.Value, key, result);
                }

                return;
            }

            if (token is JArray array)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    FlattenJsonToken(array[i], $"{prefix}[{i}]", result);
                }

                return;
            }

            result[prefix] = token is JValue value && value.Value != null
                ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty
                : token.ToString();
        }

        private static Dictionary<string, string> FlattenToml(string content)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var section = string.Empty;
            using var reader = new StringReader(content ?? string.Empty);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (trimmed.StartsWith("[", StringComparison.Ordinal) &&
                    trimmed.EndsWith("]", StringComparison.Ordinal) &&
                    trimmed.Length > 2)
                {
                    section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    continue;
                }

                var equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                var key = trimmed.Substring(0, equalsIndex).Trim();
                var value = StripTomlValue(trimmed.Substring(equalsIndex + 1).Trim());
                result[string.IsNullOrWhiteSpace(section) ? key : $"{section}.{key}"] = value;
            }

            return result;
        }

        private static string StripTomlValue(string value)
        {
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }

        private static Dictionary<string, string> FlattenLines(string content)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = (content ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                result[$"line:{i + 1:0000}"] = lines[i];
            }

            return result;
        }
    }
}
