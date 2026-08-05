using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Serialization;

namespace CyanTools.Utils
{
    internal sealed record ClashProxyDefinition(
        string Id,
        string Name,
        string Type,
        string Server,
        int Port);

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
                .IgnoreUnmatchedProperties()
                .Build();
            var document = deserializer.Deserialize<ClashSubscriptionDocument>(payload);
            foreach (var proxy in document?.Proxies ?? Enumerable.Empty<ClashProxyEntry>())
            {
                if (string.IsNullOrWhiteSpace(proxy.Server)
                    || !int.TryParse(proxy.Port, out var port)
                    || port is <= 0 or > 65535)
                {
                    continue;
                }

                var server = proxy.Server.Trim();
                var name = string.IsNullOrWhiteSpace(proxy.Name) ? $"{server}:{port}" : proxy.Name.Trim();
                var type = string.IsNullOrWhiteSpace(proxy.Type) ? "http" : proxy.Type.Trim().ToLowerInvariant();
                var id = $"subscription-{result.Count}-{name}";
                result.Add(new ClashProxyDefinition(id, name, type, server, port));
            }

            return result;
        }

        private sealed class ClashSubscriptionDocument
        {
            [YamlMember(Alias = "proxies")]
            public List<ClashProxyEntry>? Proxies { get; set; }
        }

        private sealed class ClashProxyEntry
        {
            [YamlMember(Alias = "name")]
            public string? Name { get; set; }

            [YamlMember(Alias = "type")]
            public string? Type { get; set; }

            [YamlMember(Alias = "server")]
            public string? Server { get; set; }

            [YamlMember(Alias = "port")]
            public string? Port { get; set; }
        }
    }
}
