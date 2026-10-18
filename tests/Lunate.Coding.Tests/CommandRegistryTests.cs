using System.Text.RegularExpressions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The command registry's guard (design D3): the <c>/help</c> listing and the dispatch switch stay
/// one set, so a command can never be dispatchable without a description — and never described
/// without an action.
/// </summary>
public sealed class CommandRegistryTests
{
    [Fact]
    public void The_descriptor_table_matches_the_dispatch_switch() =>
        Assert.Equal(
            [
                .. Commands
                    .Descriptors.Select(descriptor => descriptor.Name)
                    .Order(StringComparer.Ordinal),
            ],
            [.. DispatchedNames().Order(StringComparer.Ordinal)]
        );

    [Fact]
    public void Every_descriptor_carries_a_one_line_description() =>
        Assert.Empty(
            Commands
                .Descriptors.Where(descriptor =>
                    string.IsNullOrWhiteSpace(descriptor.Description)
                    || descriptor.Description.Contains('\n')
                    || descriptor.Description.EndsWith('.')
                )
                .Select(descriptor => descriptor.Name)
        );

    [Fact]
    public void Every_plain_descriptor_completes() =>
        Assert.Equal(
            [
                .. Commands
                    .Descriptors.Where(descriptor =>
                        !descriptor.Name.StartsWith(Commands.SkillPrefix, StringComparison.Ordinal)
                    )
                    .Select(descriptor => descriptor.Name)
                    .Order(StringComparer.Ordinal),
            ],
            Commands.BuiltIn
        );

    /// <summary>
    /// The names the dispatch switch acts on, read from the source: the case labels of
    /// <c>DispatchCommandAsync</c> plus the skill-invocation prefix branch (which is not a case
    /// label). The dispatcher's control flow stays where it is; this is the only way a table entry
    /// and a branch can be proven to be one set.
    /// </summary>
    private static IReadOnlyList<string> DispatchedNames()
    {
        string source = File.ReadAllText(
            Path.Combine(
                TestPaths.RepositoryRoot,
                "src",
                "Lunate.Coding",
                "InteractiveSession.Commands.cs"
            )
        );
        int start = source.IndexOf("DispatchCommandAsync", StringComparison.Ordinal);
        Assert.True(
            start >= 0,
            "the dispatch entry point was not found in InteractiveSession.Commands.cs"
        );
        string dispatch = source[start..];

        List<string> names =
        [
            .. Regex
                .Matches(dispatch, """case "(?<name>/[a-z]+)":""")
                .Select(match => match.Groups["name"].Value),
        ];
        if (dispatch.Contains("Commands.SkillPrefix", StringComparison.Ordinal))
        {
            names.Add(Commands.SkillName);
        }

        return names;
    }
}
