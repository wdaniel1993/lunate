using Lunate.Ai;

namespace Lunate.Ai.Tests;

/// <summary>
/// The state root is one rule, and a process that must not touch the user's own state depends on it:
/// the extensions host, its cache and the trust store all read <see cref="LunateHome.Store"/>.
/// </summary>
public sealed class LunateHomeTests
{
    [Fact]
    public void An_unset_or_blank_override_is_the_user_profile()
    {
        string profile = Path.Combine(Path.GetTempPath(), "a-profile");

        Assert.Equal(Path.Combine(profile, ".lunate"), LunateHome.Resolve(null, profile));
        Assert.Equal(Path.Combine(profile, ".lunate"), LunateHome.Resolve("   ", profile));
    }

    [Fact]
    public void An_absolute_override_is_the_store_and_a_trailing_separator_does_not_matter()
    {
        string store = Path.Combine(Path.GetTempPath(), "somewhere", "lunate");

        Assert.Equal(store, LunateHome.Resolve(store, "/ignored"));
        Assert.Equal(store, LunateHome.Resolve(store + Path.DirectorySeparatorChar, "/ignored"));
    }

    [Fact]
    public void A_relative_override_is_refused_by_name()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            LunateHome.Resolve("relative/state", "/ignored")
        );

        Assert.Contains("LUNATE_HOME", error.Message, StringComparison.Ordinal);
        Assert.Contains("absolute", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A store inside the tree the session governs could have its trust records written by that
    /// tree, so the refusal is exact and user-facing rather than a generic containment error.
    /// </summary>
    [Fact]
    public void A_store_inside_the_worktree_is_refused_by_the_trust_message()
    {
        string root = Path.Combine(Path.GetTempPath(), "lunate-guard", "worktree");
        string store = Path.Combine(root, ".state");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            LunateHome.EnsureOutsideWorkspace(store, root)
        );

        Assert.Equal(
            "LUNATE_HOME points inside the workspace; trust records there could be written by the repository",
            error.Message
        );
    }

    [Fact]
    public void A_store_equal_to_the_root_is_refused_trailing_separator_included()
    {
        string root = Path.Combine(Path.GetTempPath(), "lunate-guard", "worktree");

        Assert.Throws<InvalidOperationException>(() =>
            LunateHome.EnsureOutsideWorkspace(root, root)
        );
        Assert.Throws<InvalidOperationException>(() =>
            LunateHome.EnsureOutsideWorkspace(root + Path.DirectorySeparatorChar, root)
        );
        Assert.Throws<InvalidOperationException>(() =>
            LunateHome.EnsureOutsideWorkspace(root, root + Path.DirectorySeparatorChar)
        );
    }

    [Fact]
    public void A_store_in_a_sibling_directory_is_accepted()
    {
        string parent = Path.Combine(Path.GetTempPath(), "lunate-guard");
        string root = Path.Combine(parent, "worktree");
        string sibling = Path.Combine(parent, "state");

        LunateHome.EnsureOutsideWorkspace(sibling, root);
    }

    [Fact]
    public void A_store_outside_any_workspace_is_accepted()
    {
        string root = Path.Combine(Path.GetTempPath(), "lunate-guard", "worktree");
        string outside = Path.Combine(Path.GetTempPath(), "lunate-state-elsewhere", "lunate");

        LunateHome.EnsureOutsideWorkspace(outside, root);
    }

    /// <summary>A dot segment is resolved before the comparison, so it cannot hide a store.</summary>
    [Fact]
    public void Dot_and_dotdot_segments_are_resolved_before_comparing()
    {
        string parent = Path.Combine(Path.GetTempPath(), "lunate-guard");
        string root = Path.Combine(parent, "worktree");

        Assert.True(LunateHome.IsInside(Path.Combine(root, "nested", "..", ".state"), root));
        Assert.False(LunateHome.IsInside(Path.Combine(root, "..", "state"), root));
    }

    /// <summary>Windows and macOS compare case-insensitively; Linux does not.</summary>
    [Fact]
    public void The_comparison_follows_the_platforms_case_rule()
    {
        string root = Path.Combine(Path.GetTempPath(), "lunate-guard", "worktree");
        string upper = Path.Combine(root, ".STATE").ToUpperInvariant();

        Assert.Equal(!OperatingSystem.IsLinux(), LunateHome.IsInside(upper, root));
    }
}
