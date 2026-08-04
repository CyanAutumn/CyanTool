using CyanTools.Pipeline;
using CyanTools.Utils;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class PipelineSnapshotHelperTests
{
    [Fact]
    public void CaptureSimpleProperties_ExcludesCredentialLikeProperties()
    {
        var target = new CredentialSettings
        {
            InputText = "artist-id",
            DanbooruToken = "danbooru-secret",
            PixivRefreshToken = "pixiv-secret",
            Api_Key = "api-secret",
            Password = "password-secret"
        };

        Dictionary<string, object?> snapshot = PipelineSnapshotHelper.CaptureSimpleProperties(target);

        Assert.Equal("artist-id", snapshot[nameof(CredentialSettings.InputText)]);
        Assert.DoesNotContain(nameof(CredentialSettings.DanbooruToken), snapshot);
        Assert.DoesNotContain(nameof(CredentialSettings.PixivRefreshToken), snapshot);
        Assert.DoesNotContain(nameof(CredentialSettings.Api_Key), snapshot);
        Assert.DoesNotContain(nameof(CredentialSettings.Password), snapshot);
    }

    [Fact]
    public void ApplySimpleProperties_IgnoresLegacyCredentialsAndKeepsControlValues()
    {
        var target = new CredentialSettings
        {
            InputText = "old-input",
            DanbooruToken = "control-danbooru-token",
            PixivRefreshToken = "control-pixiv-token"
        };
        var legacyWorkflowSnapshot = new Dictionary<string, object?>
        {
            [nameof(CredentialSettings.InputText)] = "new-input",
            [nameof(CredentialSettings.DanbooruToken)] = "workflow-danbooru-token",
            [nameof(CredentialSettings.PixivRefreshToken)] = "workflow-pixiv-token"
        };

        PipelineSnapshotHelper.ApplySimpleProperties(target, legacyWorkflowSnapshot);

        Assert.Equal("new-input", target.InputText);
        Assert.Equal("control-danbooru-token", target.DanbooruToken);
        Assert.Equal("control-pixiv-token", target.PixivRefreshToken);
    }

    [Fact]
    public void RemoveSensitiveProperties_SanitizesCustomProviderSnapshots()
    {
        var snapshot = new Dictionary<string, object?>
        {
            ["OutputDirectory"] = "D:\\dataset",
            ["AuthorizationHeader"] = "Bearer secret",
            ["SessionCookie"] = "cookie-secret",
            ["ClientSecret"] = "client-secret"
        };

        Dictionary<string, object?> sanitized = PipelineSnapshotHelper.RemoveSensitiveProperties(snapshot);

        Assert.Single(sanitized);
        Assert.Equal("D:\\dataset", sanitized["OutputDirectory"]);
    }

    [Fact]
    public void DownloaderCredentialState_SharesCurrentControlCredentialsInMemory()
    {
        DatasetDownloaderCredentialState.PublishDanbooruToken("current-danbooru-token");
        DatasetDownloaderCredentialState.PublishPixivRefreshToken("current-pixiv-token");

        Assert.True(DatasetDownloaderCredentialState.TryGetDanbooruToken(out string danbooruToken));
        Assert.True(DatasetDownloaderCredentialState.TryGetPixivRefreshToken(out string pixivToken));
        Assert.Equal("current-danbooru-token", danbooruToken);
        Assert.Equal("current-pixiv-token", pixivToken);
    }

    private sealed class CredentialSettings
    {
        public string InputText { get; set; } = string.Empty;

        public string DanbooruToken { get; set; } = string.Empty;

        public string PixivRefreshToken { get; set; } = string.Empty;

        public string Api_Key { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }
}
