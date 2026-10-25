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

        Assert.Equal(
            Path.Combine(profile, ".lunate"),
            LunateHome.Resolve(null, profile)
        );
        Assert.Equal(
            Path.Combine(profile, ".lunate"),
            LunateHome.Resolve("   ", profile)
        );
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
}
