using Lunate.Extensibility;
using NuGet.Frameworks;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The dependency walker's rules (design: "Resolution rule"): NuGet's PackageReference answer
/// wherever NuGet resolves without a warning — the lowest applicable version, the highest minimum
/// across cousin ranges, a direct declaration left alone, a float at its highest match — and
/// NuGet's own refusal, code and fix included, wherever NuGet would warn or fail.
/// </summary>
public sealed class DependencyWalkerTests
{
    [Fact]
    public async Task A_declared_range_takes_the_lowest_version_in_it()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .Add("Acme.Lib", "1.4.0")
            .Add("Acme.Lib", "2.0.0");

        IReadOnlyList<NuGetDependencyWalker.Resolved> resolved = await ResolveAsync(
            feed,
            ("Acme.Lib", "[1.0.0,2.0.0)")
        );

        Assert.Equal("1.0.0", Version(resolved, "Acme.Lib"));
    }

    [Fact]
    public async Task A_bare_declared_version_is_a_minimum_like_a_PackageReference()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root).Add("Acme.Lib", "1.4.7");

        IReadOnlyList<NuGetDependencyWalker.Resolved> resolved = await ResolveAsync(
            feed,
            ("Acme.Lib", "1.4.0")
        );

        Assert.Equal("1.4.7", Version(resolved, "Acme.Lib"));
    }

    [Fact]
    public async Task Cousin_ranges_take_the_highest_minimum()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .Add("Acme.Lib", "1.4.0")
            .Add("Acme.Lib", "1.9.0")
            .Add("Acme.Tools", "1.0.0", dependencies: ["Acme.Lib@[1.0.0, )"])
            .Add("Acme.Utils", "1.0.0", dependencies: ["Acme.Lib@[1.4.0, )"]);

        IReadOnlyList<NuGetDependencyWalker.Resolved> resolved = await ResolveAsync(
            feed,
            ("Acme.Tools", "1.0.0"),
            ("Acme.Utils", "1.0.0")
        );

        Assert.Equal("1.4.0", Version(resolved, "Acme.Lib"));
        Assert.False(Package(resolved, "Acme.Lib").Direct);
    }

    [Fact]
    public async Task An_upper_bound_in_a_dependency_is_respected()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .Add("Acme.Lib", "1.9.0")
            .Add("Acme.Lib", "2.5.0")
            .Add("Acme.Tools", "1.0.0", dependencies: ["Acme.Lib@[1.0.0,2.0.0)"]);

        IReadOnlyList<NuGetDependencyWalker.Resolved> resolved = await ResolveAsync(
            feed,
            ("Acme.Tools", "1.0.0")
        );

        Assert.Equal("1.0.0", Version(resolved, "Acme.Lib"));
    }

    [Fact]
    public async Task A_direct_declaration_is_not_dragged_down_by_a_dependency_edge()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .Add("Acme.Lib", "1.9.0")
            .Add("Acme.Tools", "1.0.0", dependencies: ["Acme.Lib@[1.0.0,2.0.0)"]);

        IReadOnlyList<NuGetDependencyWalker.Resolved> resolved = await ResolveAsync(
            feed,
            ("Acme.Lib", "1.9.0"),
            ("Acme.Tools", "1.0.0")
        );

        Assert.Equal("1.9.0", Version(resolved, "Acme.Lib"));
        Assert.True(Package(resolved, "Acme.Lib").Direct);

        // The declaration's range is what bounds the choice; an edge inside it changes nothing.
        IReadOnlyList<NuGetDependencyWalker.Resolved> ranged = await ResolveAsync(
            feed,
            ("Acme.Lib", "[1.0.0,3.0.0)"),
            ("Acme.Tools", "1.0.0")
        );
        Assert.Equal("1.0.0", Version(ranged, "Acme.Lib"));
    }

    [Fact]
    public async Task A_direct_declaration_is_not_dragged_up_by_a_dependency_edge()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .Add("Acme.Lib", "1.4.0")
            .Add("Acme.Tools", "1.0.0", dependencies: ["Acme.Lib@[1.4.0, )"]);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                ResolveAsync(feed, ("Acme.Lib", "1.0.0"), ("Acme.Tools", "1.0.0"))
        );

        Assert.Contains("NU1605", failure.Message, StringComparison.Ordinal);

        // A range is the project's own choice too: NuGet keeps the lowest version inside it
        // rather than moving up to the edge's minimum, and reports the downgrade.
        InvalidOperationException ranged = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ResolveAsync(feed, ("Acme.Lib", "[1.0.0,3.0.0)"), ("Acme.Tools", "1.0.0"))
        );

        Assert.Contains("NU1605", ranged.Message, StringComparison.Ordinal);
        Assert.Contains("resolves to 1.0.0", ranged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_floating_declaration_takes_the_highest_match()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .Add("Acme.Lib", "1.4.0")
            .Add("Acme.Lib", "1.4.7")
            .Add("Acme.Lib", "1.9.0")
            .Add("Acme.Lib", "2.0.0");

        Assert.Equal("1.9.0", Version(await ResolveAsync(feed, ("Acme.Lib", "1.*")), "Acme.Lib"));
        Assert.Equal("1.4.7", Version(await ResolveAsync(feed, ("Acme.Lib", "1.4.*")), "Acme.Lib"));
        Assert.Equal("2.0.0", Version(await ResolveAsync(feed, ("Acme.Lib", "*")), "Acme.Lib"));
    }

    [Fact]
    public async Task A_float_that_a_dependency_edge_excludes_is_refused()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .Add("Acme.Lib", "1.4.0")
            .Add("Acme.Lib", "1.9.0")
            .Add("Acme.Tools", "1.0.0", dependencies: ["Acme.Lib@[1.0.0,1.5.0)"]);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                ResolveAsync(feed, ("Acme.Lib", "1.*"), ("Acme.Tools", "1.0.0"))
        );

        Assert.Contains("NU1608", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_downgrade_is_refused_as_NU1605_with_the_fix_hint()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .Add("Acme.Lib", "1.4.0")
            .Add("Acme.Tools", "1.0.0", dependencies: ["Acme.Lib@[1.4.0, )"]);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                ResolveAsync(feed, ("Acme.Lib", "1.0.0"), ("Acme.Tools", "1.0.0"))
        );

        Assert.Contains("NU1605", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'Acme.Lib'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("1.0.0", failure.Message, StringComparison.Ordinal);
        Assert.Contains("[1.4.0, )", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Acme.Tools 1.0.0", failure.Message, StringComparison.Ordinal);
        Assert.EndsWith(
            "add #:package Acme.Lib@1.4.0 satisfying all ranges",
            failure.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task The_fix_hint_names_a_version_the_feed_publishes()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .Add("Acme.Lib", "2.0.1")
            .Add("Acme.Alpha", "1.0.0", dependencies: ["Acme.Lib@[1.0.0,1.5.0)"])
            .Add("Acme.Beta", "1.0.0", dependencies: ["Acme.Lib@[2.0.0,3.0.0)"]);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                ResolveAsync(feed, ("Acme.Alpha", "1.0.0"), ("Acme.Beta", "1.0.0"))
        );

        Assert.EndsWith(
            "add #:package Acme.Lib@2.0.1 satisfying all ranges",
            failure.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task A_version_outside_a_dependency_range_is_refused_as_NU1608_with_the_fix_hint()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .Add("Acme.Lib", "1.4.0")
            .Add("Acme.Lib", "2.0.0")
            .Add("Acme.Tools", "1.0.0", dependencies: ["Acme.Lib@[1.0.0,2.0.0)"]);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                ResolveAsync(feed, ("Acme.Lib", "2.0.0"), ("Acme.Tools", "1.0.0"))
        );

        Assert.Contains("NU1608", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'Acme.Lib'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Acme.Lib@2.0.0", failure.Message, StringComparison.Ordinal);
        Assert.Contains("[1.0.0, 2.0.0)", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Acme.Tools 1.0.0", failure.Message, StringComparison.Ordinal);
        Assert.EndsWith(
            "add #:package Acme.Lib@1.0.0 satisfying all ranges",
            failure.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Cousins_that_cannot_agree_are_refused_as_NU1107_with_every_range_and_asker()
    {
        using var temp = new TempDirectory();
        var feed = new TestPackageFeed(temp.Root)
            .Add("Acme.Lib", "1.0.0")
            .Add("Acme.Lib", "2.0.0")
            .Add("Acme.Alpha", "1.0.0", dependencies: ["Acme.Lib@[1.0.0,1.5.0)"])
            .Add("Acme.Beta", "1.0.0", dependencies: ["Acme.Lib@[2.0.0,3.0.0)"]);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                ResolveAsync(feed, ("Acme.Alpha", "1.0.0"), ("Acme.Beta", "1.0.0"))
        );

        Assert.Contains("NU1107", failure.Message, StringComparison.Ordinal);
        Assert.Contains("[1.0.0, 1.5.0)", failure.Message, StringComparison.Ordinal);
        Assert.Contains("[2.0.0, 3.0.0)", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Acme.Alpha 1.0.0", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Acme.Beta 1.0.0", failure.Message, StringComparison.Ordinal);
        Assert.EndsWith(
            "add #:package Acme.Lib@2.0.0 satisfying all ranges",
            failure.Message,
            StringComparison.Ordinal
        );
    }

    private static async Task<IReadOnlyList<NuGetDependencyWalker.Resolved>> ResolveAsync(
        TestPackageFeed feed,
        params (string Id, string Range)[] declared
    )
    {
        NuGetPackageFeed packageFeed = NuGetPackageFeed.Create(
            feed.Root,
            [feed.Source],
            observer: null
        );
        return await new NuGetDependencyWalker(
            packageFeed,
            NuGetFramework.Parse("net10.0")
        ).ResolveAsync(
            [
                .. declared.Select(package => new ExtensionPackageReference(
                    package.Id,
                    package.Range,
                    FormattableString.Invariant($"{package.Id}@{package.Range}")
                )),
            ],
            TestContext.Current.CancellationToken
        );
    }

    private static NuGetDependencyWalker.Resolved Package(
        IReadOnlyList<NuGetDependencyWalker.Resolved> resolved,
        string id
    ) => Assert.Single(resolved, package => package.Id == id);

    private static string Version(
        IReadOnlyList<NuGetDependencyWalker.Resolved> resolved,
        string id
    ) => Package(resolved, id).Version.ToNormalizedString();
}
