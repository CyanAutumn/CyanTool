using CyanTools.Utils;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class TagTextMergeHelperTests
{
    [Fact]
    public void ResolveTags_MergeUnique_SplitsLegacySeparatorsAndDeduplicates()
    {
        var result = TagTextMergeHelper.ResolveTags(
            "tag_a; tag b，tag_c\r\ntag_d",
            new[] { "tag a", "tag_e" },
            ExistingTxtHandlingMode.MergeUnique);

        Assert.Equal(
            new[] { "tag a", "tag b", "tag c", "tag d", "tag e" },
            result);
    }
}
