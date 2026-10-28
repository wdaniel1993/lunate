namespace Lunate.Coding.Tests;

/// <summary>
/// The wait-ceiling rule, as a source scan (the shape <c>HomeResolutionChecker</c> uses): outside
/// <see cref="WaitBudgets"/>, no file in this project may carry a duration literal that bounds how
/// long a test waits. A literal at a call site is sized once, for one machine, and is invisible to
/// every other test waiting on the same condition — which is how three separate tests failed on a
/// clock instead of on behaviour (#225, #231, #258) before the numbers had one home.
/// <para>
/// Two forms are caught. A <c>TimeSpan.From*</c> call with a numeric argument, wherever it sits, is
/// a duration the sweep either moved into <see cref="WaitBudgets"/> or has to justify here. An
/// <c>attempt &lt; 600</c> poll loop is the same ceiling written as a count of hundred-millisecond
/// sleeps: nothing about the machine it runs on is visible in it, and its implied window drifts from
/// the message the timeout prints.
/// </para>
/// <para>
/// The allow-list is per file and per kind, and every entry carries a reason:
/// <see cref="WaitCeilingTests.Every_allowed_site_carries_a_reason"/> enforces it. The listed
/// numbers are not ceilings — a simulated clock, an input to the code under test, a performance
/// assertion — and a new duration literal in one of those files still fails the scan.
/// </para>
/// </summary>
internal static class WaitCeilingChecker
{
    /// <summary>The one file allowed to hold a duration: it is the rule.</summary>
    internal const string BudgetsFile = "WaitBudgets.cs";

    /// <summary>The scanner names its own patterns, so it must not scan itself.</summary>
    internal const string CheckerFile = "WaitCeilingChecker.cs";

    /// <summary>What a flagged line is doing with its number.</summary>
    internal enum Signal
    {
        /// <summary>A <c>TimeSpan.From*</c> call with a numeric argument.</summary>
        Duration,

        /// <summary><c>Process.WaitForExit</c> with an integer of milliseconds.</summary>
        ExitPoll,

        /// <summary>A poll loop bounded by a count of attempts rather than by a budget.</summary>
        AttemptLoop,
    }

    internal sealed record AllowedSite(string File, Signal Signal, string Reason);

    internal static IReadOnlyList<AllowedSite> AllowedSites { get; } =
    [
        // The delay is the whole assertion: a wait that names no budget must outlive the 10 s window
        // every defaulted wait used to inherit, and there is no seam to move the clock through.
        new(
            "WaitBudgetTests.cs",
            Signal.Duration,
            "the deliberate delay is the assertion: an unnamed wait must outlive the old window"
        ),
        // The slow hook's threshold and its handler's delay are the behaviour under test, not a
        // bound on the wait: the notice fires because the hook crossed 20 ms after 80 ms of work.
        new(
            "SlowHookNoticeTests.cs",
            Signal.Duration,
            "the slow hook's 20 ms threshold and 80 ms handler delay are the behaviour under test"
        ),
        new(
            "SlowHookNoticeClearTests.cs",
            Signal.Duration,
            "the slow hook's 20 ms threshold and 80 ms handler delay are the behaviour under test"
        ),
        new(
            "LateFollowUpTests.cs",
            Signal.Duration,
            "the 600 ms hook delay is what makes the follow-up late; the test is about that lateness"
        ),
        new(
            "ExtensionSessionTests.cs",
            Signal.Duration,
            "an elapsed-time assertion: the recorded load time has to be this load's"
        ),
        new(
            "EditToolTests.cs",
            Signal.Duration,
            "an elapsed-time assertion about the edit's own work"
        ),
        new(
            "FixtureRestoreEnvironmentTests.cs",
            Signal.Duration,
            "durations recorded in a restore report fixture, plus the drain window that simulates a "
                + "stream which never closes"
        ),
        new(
            "WelcomeHeaderBudgetTests.cs",
            Signal.Duration,
            "a performance budget the assertion compares against, not a wait"
        ),
        new(
            "ExtensionsViewHookTimingTests.cs",
            Signal.Duration,
            "remote-start stamps are the input to a pure timing function"
        ),
        new(
            "ExtensionsViewTestSupport.cs",
            Signal.Duration,
            "load durations reported by a fake extension state, not waited on"
        ),
        new(
            "InteractiveSessionSecretPromptTests.cs",
            Signal.Duration,
            "the simulated frame step handed to the scheduler, not a wait"
        ),
        new(
            "ManualSchedulerChainProbe.cs",
            Signal.Duration,
            "the simulated frame step handed to the scheduler, not a wait"
        ),
    ];

    /// <summary>The violations, as <c>path:line: detail</c>, in file order.</summary>
    internal static IReadOnlyList<string> FindViolations(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var violations = new List<string>();
        foreach (string file in SourceFiles(repositoryRoot))
        {
            string name = Path.GetFileName(file);
            if (
                string.Equals(name, BudgetsFile, StringComparison.Ordinal)
                || string.Equals(name, CheckerFile, StringComparison.Ordinal)
            )
            {
                continue;
            }

            string relative = Relative(repositoryRoot, file);
            string[] lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (Classify(lines[index]) is not { } signal)
                {
                    continue;
                }

                if (
                    AllowedSites.Any(site =>
                        site.Signal == signal
                        && string.Equals(site.File, relative, StringComparison.Ordinal)
                    )
                )
                {
                    continue;
                }

                violations.Add(
                    $"{relative}:{index + 1}: {signal} bounds a wait with a literal; move it to WaitBudgets under the condition it waits for"
                );
            }
        }

        return violations;
    }

    /// <summary>Every <c>.cs</c> file of this test project, without build output.</summary>
    internal static IReadOnlyList<string> SourceFiles(string repositoryRoot)
    {
        string root = Path.Combine(repositoryRoot, "tests", "Lunate.Coding.Tests");
        return
        [
            .. Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(file))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>The project-relative path, with forward slashes, as the allow-list writes it.</summary>
    internal static string Relative(string repositoryRoot, string file) =>
        Path.GetRelativePath(Path.Combine(repositoryRoot, "tests", "Lunate.Coding.Tests"), file)
            .Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsBuildOutput(string file)
    {
        string[] segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("obj", StringComparer.Ordinal)
            || segments.Contains("bin", StringComparer.Ordinal);
    }

    private static Signal? Classify(string line)
    {
        if (HasNumericTimeSpan(line))
        {
            return Signal.Duration;
        }

        if (HasNumericWaitForExit(line))
        {
            return Signal.ExitPoll;
        }

        return HasAttemptCeiling(line) ? Signal.AttemptLoop : null;
    }

    /// <summary>
    /// A <c>TimeSpan.FromSeconds</c> (or <c>FromMinutes</c>/<c>FromMilliseconds</c>) call whose
    /// argument starts with a digit: <c>FromMilliseconds(frames)</c> passes a value through and is
    /// not a ceiling written here.
    /// </summary>
    private static bool HasNumericTimeSpan(string line)
    {
        string[] calls = ["FromSeconds(", "FromMinutes(", "FromMilliseconds("];
        foreach (string call in calls)
        {
            int at = line.IndexOf(call, StringComparison.Ordinal);
            while (at >= 0)
            {
                int argument = at + call.Length;
                if (argument < line.Length && char.IsAsciiDigit(line[argument]))
                {
                    return true;
                }

                at = line.IndexOf(call, argument, StringComparison.Ordinal);
            }
        }

        return false;
    }

    /// <summary><c>WaitForExit(15000)</c>: the same ceiling in whole milliseconds.</summary>
    private static bool HasNumericWaitForExit(string line)
    {
        const string call = "WaitForExit(";
        int at = line.IndexOf(call, StringComparison.Ordinal);
        return at >= 0
            && at + call.Length < line.Length
            && char.IsAsciiDigit(line[at + call.Length]);
    }

    /// <summary><c>attempt &lt; 600</c>: a ceiling counted in poll intervals instead of seconds.</summary>
    private static bool HasAttemptCeiling(string line)
    {
        const string call = "attempt < ";
        int at = line.IndexOf(call, StringComparison.Ordinal);
        return at >= 0
            && at + call.Length < line.Length
            && char.IsAsciiDigit(line[at + call.Length]);
    }
}
