using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using CyanTools.Utils;
using YamlDotNet.Serialization;
using Xunit;

namespace CyanTools.Tests;

public sealed class ProxySettingsStoreTests
{
    [Fact]
    public async Task RefreshSubscription_PersistsNodesAndSelectionWithoutRefreshingOnLoad()
    {
        var settingsDirectory = Path.Combine(Path.GetTempPath(), $"cyantools-proxy-tests-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(settingsDirectory, "proxy_settings.json");
        var downloadCount = 0;
        const string yaml = """
            proxies:
              - name: Saved node
                type: ss
                server: saved.example.com
                port: 443
                client-fingerprint: chrome
            """;

        Task<string> LoadSubscription(Uri _, CancellationToken __)
        {
            downloadCount++;
            return Task.FromResult(yaml);
        }

        static Task<int?> ProbeNode(ProxyNode _, CancellationToken __)
            => Task.FromResult<int?>(42);

        try
        {
            var first = new ProxySettingsViewModel(settingsPath, LoadSubscription, ProbeNode)
            {
                SubscriptionUrl = "https://example.invalid/subscription"
            };

            await first.RefreshSubscriptionAsync();
            first.SelectedNode = Assert.Single(first.Nodes, node => !node.IsLocal);
            var persistedJson = File.ReadAllText(settingsPath);

            var restored = new ProxySettingsViewModel(settingsPath, LoadSubscription, ProbeNode);
            var restoredNode = Assert.Single(restored.Nodes, node => !node.IsLocal);

            Assert.Equal(1, downloadCount);
            Assert.DoesNotContain("https://example.invalid/subscription", persistedJson, StringComparison.Ordinal);
            Assert.DoesNotContain("client-fingerprint", persistedJson, StringComparison.Ordinal);
            Assert.Equal("Saved node", restoredNode.Name);
            Assert.Same(restoredNode, restored.SelectedNode);
            Assert.True(restoredNode.UsesManagedCore);
            Assert.Contains("client-fingerprint", restoredNode.MihomoProxyYaml, StringComparison.Ordinal);

            await restored.TestNodesAsync();

            Assert.Equal("42 ms（托管内核）", restoredNode.AvailabilityText);
            Assert.Contains("2/2", restored.StatusText);
        }
        finally
        {
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildRuntimeConfig_UsesOnlyTheSelectedNodeAndPrivateMixedPort()
    {
        const string yaml = """
            proxies:
              - name: Managed node
                type: ss
                server: managed.example.com
                port: 8443
                udp: true
            """;
        var definition = Assert.Single(ClashSubscriptionParser.Parse(yaml));
        var node = new ProxyNode(
            definition.Id,
            definition.Name,
            definition.Type,
            definition.Server,
            definition.Port,
            mihomoProxyYaml: definition.RawYaml);

        var runtimeYaml = ManagedMihomoService.BuildRuntimeConfig(node, 32123);
        var config = new DeserializerBuilder()
            .Build()
            .Deserialize<Dictionary<object, object>>(runtimeYaml);

        Assert.Equal("32123", config["mixed-port"].ToString());
        Assert.Equal("false", config["allow-lan"].ToString());
        Assert.Contains("Managed node", runtimeYaml, StringComparison.Ordinal);
        Assert.Contains("MATCH,CYANTOOLS", runtimeYaml, StringComparison.Ordinal);
        Assert.Contains("udp: true", runtimeYaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseReleaseAssetSha256_UsesDigestFromTheMatchingAssetRow()
    {
        const string assetName = "mihomo-windows-amd64-compatible-v2.3.4.zip";
        const string expectedHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var html = $"""
            <li><span>other.zip</span><span>sha256:{new string('a', 64)}</span></li>
            <li><a>{assetName}</a><span>sha256:{expectedHash}</span></li>
            """;

        var digest = ManagedMihomoService.ParseReleaseAssetSha256(html, assetName);

        Assert.Equal(expectedHash.ToUpperInvariant(), digest);
    }

    [Theory]
    [InlineData("v1.19.29", "1.19.29")]
    [InlineData("2.0.1", "2.0.1")]
    public void NormalizeVersion_AcceptsStableSemanticVersions(string input, string expected)
        => Assert.Equal(expected, ManagedMihomoService.NormalizeVersion(input));

    [Theory]
    [InlineData("latest")]
    [InlineData("1.19.29-beta")]
    [InlineData("../1.19.29")]
    public void NormalizeVersion_RejectsUnsafeOrPrereleaseValues(string input)
        => Assert.Throws<InvalidDataException>(() => ManagedMihomoService.NormalizeVersion(input));

    [Fact]
    public async Task ManagedMihomoLease_CanBeReleasedAfterServiceShutdown()
    {
        var service = new ManagedMihomoService(Path.Combine(Path.GetTempPath(), $"cyantools-mihomo-tests-{Guid.NewGuid():N}"));
        var lease = CreateLeaseForLifecycleTest(service);

        service.Dispose();

        await lease.DisposeAsync();
    }

    [Fact]
    public void ParseClashProxies_ReadsBlockAndFlowEntriesOnlyFromProxies()
    {
        const string yaml = """
            mixed-port: 7890
            proxies:
              - name: "Tokyo: premium, 01"
                type: ss
                server: jp.example.com
                port: 443
                cipher: aes-128-gcm
                password: ignored
              - { name: 'US, west: 02', type: vless, server: us.example.com, port: '8443', uuid: ignored }
            proxy-groups:
              - name: Auto
                type: url-test
                proxies:
                  - "Tokyo: premium, 01"
            rules:
              - MATCH,Auto
            """;

        var nodes = ClashSubscriptionParser.Parse(yaml);

        Assert.Collection(
            nodes,
            node =>
            {
                Assert.Equal("Tokyo: premium, 01", node.Name);
                Assert.Equal("ss", node.Type);
                Assert.Equal("jp.example.com", node.Server);
                Assert.Equal(443, node.Port);
            },
            node =>
            {
                Assert.Equal("US, west: 02", node.Name);
                Assert.Equal("vless", node.Type);
                Assert.Equal("us.example.com", node.Server);
                Assert.Equal(8443, node.Port);
            });
    }

    [Fact]
    public void ParseClashProxies_SkipsInvalidEndpointsAndBuildsFallbackName()
    {
        const string yaml = """
            proxies:
              - type: http
                server: proxy.example.com
                port: 8080
              - name: Missing server
                type: socks5
                port: 1080
              - name: Invalid port
                type: http
                server: invalid.example.com
                port: 70000
            """;

        var node = Assert.Single(ClashSubscriptionParser.Parse(yaml));

        Assert.Equal("proxy.example.com:8080", node.Name);
        Assert.Equal("http", node.Type);
    }

    private static ManagedMihomoService.ManagedMihomoLease CreateLeaseForLifecycleTest(ManagedMihomoService service)
    {
        var constructor = typeof(ManagedMihomoService.ManagedMihomoLease).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        return (ManagedMihomoService.ManagedMihomoLease)constructor.Invoke(new object[]
        {
            service,
            "lifecycle-test",
            new ProxyNode("transport", "Transport", "http", "127.0.0.1", 1)
        });
    }
}
