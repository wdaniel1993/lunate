namespace Lunate.Extensibility.Tests;

/// <summary>
/// The single-file trust question (spec: "Trust before restore" and "Build-time code execution is
/// opt-in"): it names the file, its id and version, every declared package "and their
/// dependencies" — the transitive set cannot be known before a restore — and it mentions build-time
/// code only when the header opted in.
/// </summary>
public sealed class SingleFileTrustQuestionTests
{
    private static SingleFileTrustRequest Request(bool generators = false, bool changed = false) =>
        new()
        {
            Path = "/home/user/.lunate/extensions/acme.cs",
            Id = "acme-tools",
            Version = "1.0.0",
            Packages = ["Octokit@14.0.0", "NJsonSchema@[11.0.0,12.0.0)"],
            Generators = generators,
            Changed = changed,
        };

    [Fact]
    public void The_question_names_the_file_and_what_it_declares()
    {
        SingleFileTrustRequest request = Request();

        string title = SingleFileTrustQuestion.Title(request);
        Assert.Contains("'/home/user/.lunate/extensions/acme.cs'", title, StringComparison.Ordinal);
        Assert.Contains("acme-tools", title, StringComparison.Ordinal);
        Assert.Contains("1.0.0", title, StringComparison.Ordinal);
        Assert.DoesNotContain("changed since", title, StringComparison.Ordinal);

        string details = string.Join("\n", SingleFileTrustQuestion.Details(request));
        Assert.Contains("Octokit@14.0.0", details, StringComparison.Ordinal);
        Assert.Contains("NJsonSchema@[11.0.0,12.0.0)", details, StringComparison.Ordinal);
        Assert.Contains("and their dependencies", details, StringComparison.Ordinal);
    }

    [Fact]
    public void A_changed_file_says_the_earlier_decision_does_not_apply()
    {
        string title = SingleFileTrustQuestion.Title(Request(changed: true));

        Assert.Contains("changed since it was trusted", title, StringComparison.Ordinal);
        Assert.Contains("does not apply", title, StringComparison.Ordinal);
    }

    [Fact]
    public void The_title_ends_with_exactly_one_question_mark()
    {
        foreach (bool changed in new[] { false, true })
        {
            string title = SingleFileTrustQuestion.Title(Request(changed: changed));

            Assert.True(title.EndsWith('?'), $"the title '{title}' does not ask a question");
            Assert.False(
                title.EndsWith("??", StringComparison.Ordinal),
                $"the title '{title}' carries two question marks"
            );
        }
    }

    [Fact]
    public void Build_time_code_is_named_only_when_the_header_opted_in()
    {
        string plain = string.Join("\n", SingleFileTrustQuestion.Details(Request()));
        string optedIn = string.Join(
            "\n",
            SingleFileTrustQuestion.Details(Request(generators: true))
        );

        Assert.DoesNotContain("build-time", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("generator", plain, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("build-time code", optedIn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("analyzers", optedIn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source generators", optedIn, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_file_with_no_packages_says_so_rather_than_listing_nothing()
    {
        SingleFileTrustRequest request = Request() with { Packages = [] };

        string details = string.Join("\n", SingleFileTrustQuestion.Details(request));

        Assert.Contains("no NuGet packages", details, StringComparison.Ordinal);
        Assert.DoesNotContain("and their dependencies", details, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lock_change_is_shown_line_by_line()
    {
        SingleFileTrustRequest request = Request() with
        {
            LockChanges = ["~ Octokit 14.0.0 -> 14.1.0", "~ runtime 10.0.13 -> 10.0.14"],
        };

        string details = string.Join("\n", SingleFileTrustQuestion.Details(request));

        Assert.Contains("would change its lock file", details, StringComparison.Ordinal);
        Assert.Contains("~ Octokit 14.0.0 -> 14.1.0", details, StringComparison.Ordinal);
        Assert.Contains("~ runtime 10.0.13 -> 10.0.14", details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_prompt_that_does_not_know_about_single_files_never_approves_one()
    {
        IExtensionTrustPrompt packagedOnly = new PackagedOnlyPrompt();

        bool approved = await packagedOnly.ApproveSingleFileAsync(
            Request(),
            CancellationToken.None
        );

        Assert.False(approved);
    }

    /// <summary>A prompt written before single-file extensions existed: it overrides only the packaged question.</summary>
    private sealed class PackagedOnlyPrompt : IExtensionTrustPrompt
    {
        public ValueTask<bool> ApproveAsync(
            ExtensionTrustRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }
    }
}
