using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The shared child-environment scrubber: exactly the base set plus pass-through plus explicit
/// values — a parent key absent from the parent is absent from the child, and nothing leaks.
/// </summary>
public sealed class EnvironmentScrubberTests
{
    private static readonly IReadOnlyDictionary<string, string> Parent = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["PATH"] = "/usr/bin",
        ["HOME"] = "/home/user",
        ["USERPROFILE"] = @"C:\Users\user",
        ["SystemRoot"] = @"C:\Windows",
        ["TEMP"] = "/tmp",
        ["TMP"] = "/tmp",
        ["LANG"] = "en_US.UTF-8",
        ["ANTHROPIC_API_KEY"] = "sk-secret",
        ["TMPDIR"] = "/var/tmp",
    };

    private static Func<string, string?> Read(IReadOnlyDictionary<string, string> source) =>
        name => source.TryGetValue(name, out string? value) ? value : null;

    [Fact]
    public void Windows_replaces_a_case_differing_name_instead_of_duplicating_it()
    {
        IReadOnlyDictionary<string, string> environment = EnvironmentScrubber.Build(
            read: name => name == "PATH" ? "/usr/bin" : null,
            set: new Dictionary<string, string> { ["Path"] = "over" }
        );

        if (OperatingSystem.IsWindows())
        {
            // Windows environment names are case-insensitive: the declared value replaces the
            // base one instead of coexisting with it.
            Assert.Single(environment);
            Assert.Equal("over", environment["PATH"]);
        }
        else
        {
            Assert.Equal(2, environment.Count);
        }
    }

    [Fact]
    public void The_base_set_is_present_and_nothing_else_inherits()
    {
        IReadOnlyDictionary<string, string> environment = EnvironmentScrubber.Build(Read(Parent));

        Assert.Equal("/usr/bin", environment["PATH"]);
        Assert.Equal("/home/user", environment["HOME"]);
        Assert.Equal(@"C:\Users\user", environment["USERPROFILE"]);
        Assert.Equal(@"C:\Windows", environment["SystemRoot"]);
        Assert.Equal("/tmp", environment["TEMP"]);
        Assert.Equal("/tmp", environment["TMP"]);
        Assert.Equal("en_US.UTF-8", environment["LANG"]);
        Assert.Equal("/var/tmp", environment["TMPDIR"]);
        Assert.DoesNotContain("ANTHROPIC_API_KEY", environment.Keys);
        Assert.Equal(EnvironmentScrubber.BaseNames.Count, environment.Count);
    }

    [Fact]
    public void A_passthrough_name_absent_from_the_parent_stays_absent()
    {
        IReadOnlyDictionary<string, string> environment = EnvironmentScrubber.Build(
            Read(Parent),
            passthrough: ["GITHUB_TOKEN", "HOME"]
        );

        Assert.DoesNotContain("GITHUB_TOKEN", environment.Keys);
        Assert.Equal("/home/user", environment["HOME"]);
    }

    [Fact]
    public void An_explicit_value_wins_over_the_parent()
    {
        IReadOnlyDictionary<string, string> environment = EnvironmentScrubber.Build(
            Read(Parent),
            set: new Dictionary<string, string> { ["LANG"] = "de_AT.UTF-8", ["EXTRA"] = "set" }
        );

        Assert.Equal("de_AT.UTF-8", environment["LANG"]);
        Assert.Equal("set", environment["EXTRA"]);
    }

    [Fact]
    public void Empty_base_values_are_skipped()
    {
        IReadOnlyDictionary<string, string> environment = EnvironmentScrubber.Build(
            Read(new Dictionary<string, string> { ["PATH"] = "", ["LANG"] = "C" })
        );

        Assert.DoesNotContain("PATH", environment.Keys);
        Assert.Equal("C", environment["LANG"]);
        Assert.Single(environment);
    }
}
