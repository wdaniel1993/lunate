using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// Where packages are restored (ADR 0017 addendum: NuGet's global packages folder, never a
/// Lunate-owned copy): an explicit override, then configuration, then <c>NUGET_PACKAGES</c>, then
/// the user profile default. The precedence is a pure function so the rule is testable without
/// mutating a process-wide environment variable.
/// </summary>
public sealed class GlobalPackagesFolderTests
{
    private const string Home = "/home/tester";

    [Fact]
    public void The_explicit_override_wins_over_everything()
    {
        Assert.Equal(
            Path.GetFullPath("/override/packages"),
            GlobalPackagesFolder.Resolve("/override/packages", "/env/packages", Home)
        );
    }

    [Fact]
    public void Configuration_wins_over_the_environment_variable()
    {
        Assert.Equal(
            Path.GetFullPath("/config/packages"),
            GlobalPackagesFolder.Resolve("/config/packages", "/env/packages", Home)
        );
    }

    [Fact]
    public void Nuget_packages_is_honored_when_nothing_is_configured()
    {
        Assert.Equal(
            Path.GetFullPath("/env/packages"),
            GlobalPackagesFolder.Resolve(null, "/env/packages", Home)
        );
    }

    [Fact]
    public void The_default_is_the_user_profile_packages_folder()
    {
        Assert.Equal(
            Path.GetFullPath(Path.Combine(Home, ".nuget", "packages")),
            GlobalPackagesFolder.Resolve(null, null, Home)
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_value_falls_through_to_the_next_rule(string configured)
    {
        Assert.Equal(
            Path.GetFullPath("/env/packages"),
            GlobalPackagesFolder.Resolve(configured, "/env/packages", Home)
        );
    }
}
