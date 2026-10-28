using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>
/// <see cref="WaitBudgets"/> is the one home for a wait ceiling, and this is what keeps it one: a
/// source scan fails on a duration literal at a call site, the allow-list is per file and per kind
/// so a new number in an allow-listed file still fails, and every budget has to explain what it
/// waits for. Without the scan the rule is a convention that survives until the next flake is fixed
/// in a hurry — which is exactly how the numbers #231 established went back to being literals.
/// </summary>
public sealed class WaitCeilingTests
{
    [Fact]
    public void No_call_site_bounds_a_wait_with_a_literal()
    {
        IReadOnlyList<string> violations = WaitCeilingChecker.FindViolations(RepositoryRoot.Find());

        Assert.True(
            violations.Count == 0,
            "Wait durations outside WaitBudgets:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations)
        );
    }

    /// <summary>An allow-list entry without a reason is a bug, not documentation debt.</summary>
    [Fact]
    public void Every_allowed_site_carries_a_reason()
    {
        string repositoryRoot = RepositoryRoot.Find();

        foreach (WaitCeilingChecker.AllowedSite site in WaitCeilingChecker.AllowedSites)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(site.Reason),
                $"{site.File} ({site.Signal}) is allow-listed without a reason"
            );
            Assert.True(
                File.Exists(
                    Path.Combine(
                        repositoryRoot,
                        "tests",
                        "Lunate.Coding.Tests",
                        site.File.Replace('/', Path.DirectorySeparatorChar)
                    )
                ),
                $"{site.File} is allow-listed but does not exist"
            );
        }
    }

    /// <summary>
    /// Every budget says what it waits for. The scan cannot judge the prose, so it asks for prose: a
    /// field with no summary, or a one-line one, is a number with no measurement behind it, and the
    /// next person sizing it has nothing to go on.
    /// </summary>
    [Fact]
    public void Every_budget_explains_what_it_waits_for()
    {
        string path = Path.Combine(
            RepositoryRoot.Find(),
            "tests",
            "Lunate.Coding.Tests",
            WaitCeilingChecker.BudgetsFile
        );
        string[] lines = File.ReadAllLines(path);

        var unexplained = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Contains("public static readonly TimeSpan", StringComparison.Ordinal))
            {
                continue;
            }

            var documented = 0;
            for (var back = index - 1; back >= 0; back--)
            {
                string line = lines[back].TrimStart();
                if (line.StartsWith("///", StringComparison.Ordinal))
                {
                    documented++;
                    continue;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                break;
            }

            if (documented < 3)
            {
                unexplained.Add(
                    $"{WaitCeilingChecker.BudgetsFile}:{index + 1}: {lines[index].Trim()}"
                );
            }
        }

        Assert.True(
            unexplained.Count == 0,
            "WaitBudgets members with no summary explaining what they wait for:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, unexplained)
        );
    }
}
