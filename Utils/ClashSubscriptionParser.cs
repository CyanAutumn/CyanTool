using System;
using System.Collections.Generic;
using System.Globalization;
using YamlDotNet.Serialization;

namespace CyanTools.Utils
{
    internal sealed record ClashProxyDefinition(
        string Id,
        string Name,
        string Type,
        string Server,
        int Port,
        string RawYaml);

    internal static class ClashSubscriptionParser
    {
        public static IReadOnlyList<ClashProxyDefinition> Parse(string payload)
        {
            var result = new List<ClashProxyDefinition>();
            if (string.IsNullOrWhiteSpace(payload) || payload.Length > 8_000_000)
            {
                return result;
            }

            var deserializer = new DeserializerBuilder()
                .Build();
            var serializer = new SerializerBuilder()
                .DisableAliases()
                .Build();
            var document = deserializer.Deserialize<Dictionary<object, object>>(payload);
            if (document == null
                || !TryGetValue(document, "proxies", out var proxyValue)
                || proxyValue is not IEnumerable<object> proxies)
            {
                return result;
            }

            foreach (var entry in proxies)
            {
                if (entry is not Dictionary<object, object> proxy)
                {
                    continue;
                }

                var serverValue = GetScalar(proxy, "server");
                var portValue = GetScalar(proxy, "port");
                if (string.IsNullOrWhiteSpace(serverValue)
                    || !int.TryParse(portValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
                    || port is <= 0 or > 65535)
                {
                    continue;
                }

                var server = serverValue.Trim();
                var nameValue = GetScalar(proxy, "name");
                var typeValue = GetScalar(proxy, "type");
                var name = string.IsNullOrWhiteSpace(nameValue) ? $"{server}:{port}" : nameValue.Trim();
                var type = string.IsNullOrWhiteSpace(typeValue) ? "http" : typeValue.Trim().ToLowerInvariant();
                var id = $"subscription-{result.Count}-{name}";
                result.Add(new ClashProxyDefinition(id, name, type, server, port, serializer.Serialize(proxy)));
            }

            return result;
        }

        private static string? GetScalar(Dictionary<object, object> mapping, string key)
            => TryGetValue(mapping, key, out var value)
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : null;

        private static bool TryGetValue(Dictionary<object, object> mapping, string key, out object? value)
        {
            foreach (var pair in mapping)
            {
                if (string.Equals(Convert.ToString(pair.Key, CultureInfo.InvariantCulture), key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }
    }
}
