using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The reference-pack version rule (spec: "Reference assemblies for compilation"): the exact
/// runtime version when the feed publishes it, otherwise the lowest published pack of the same
/// major/minor at or above the runtime. A pack older than the runtime is never chosen, because it
/// cannot describe the runtime the host is running on.
/// </summary>
public sealed class ReferencePackRuleTests
{
    [Fact]
    public void The_exact_runtime_version_wins_when_it_is_published()
    {
        (string Version, string Rule)? selected = ReferencePackRule.Select(
            new Version(10, 0, 5),
            ["10.0.1", "10.0.5", "10.0.6"]
        );

        Assert.NotNull(selected);
        Assert.Equal("10.0.5", selected.Value.Version);
        Assert.Equal(ReferencePackRules.Exact, selected.Value.Rule);
    }

    [Fact]
    public void The_lowest_published_version_at_or_above_the_runtime_is_the_fallback()
    {
        (string Version, string Rule)? selected = ReferencePackRule.Select(
            new Version(10, 0, 7),
            ["10.0.9", "10.0.8", "10.0.3"]
        );

        Assert.NotNull(selected);
        Assert.Equal("10.0.8", selected.Value.Version);
        Assert.Equal(ReferencePackRules.NearestAtLeast, selected.Value.Rule);
    }

    [Fact]
    public void A_pack_older_than_the_runtime_is_never_chosen()
    {
        Assert.Null(ReferencePackRule.Select(new Version(10, 0, 9), ["10.0.4", "10.0.8"]));
    }

    [Fact]
    public void Another_major_or_minor_is_not_a_candidate()
    {
        Assert.Null(ReferencePackRule.Select(new Version(10, 1, 0), ["10.0.9", "11.0.0"]));
    }

    [Fact]
    public void A_prerelease_pack_is_not_a_reference_pack()
    {
        Assert.Null(ReferencePackRule.Select(new Version(10, 0, 5), ["10.0.5-preview.1"]));
    }

    [Theory]
    [InlineData("9.9.9")]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void Text_that_is_not_a_version_is_ignored(string candidate)
    {
        Assert.Null(ReferencePackRule.Select(new Version(10, 0, 5), [candidate]));
    }

    [Fact]
    public void The_reference_pack_is_the_runtime_owned_package()
    {
        Assert.Equal("Microsoft.NETCore.App.Ref", ReferencePackRule.PackageId);
    }
}
