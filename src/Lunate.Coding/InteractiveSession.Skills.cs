using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Tui;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>
    /// The session skill step: discovery (project, user and the loaded extensions' sources),
    /// the project trust question, one-time warnings, the index composition (built once per
    /// session) and the recorded <c>skills</c> prompt-section entry. Runs at session start and
    /// again only on a wholesale reload (<c>/new</c>, resume).
    /// </summary>
    private async Task ComposeSkillsAsync(CancellationToken ct)
    {
        _skills = SkillCatalog.Discover(
            new SkillScanRequest(
                _workspace.WorktreeRoot,
                _workspace.RepoRoot,
                _options.HomeDirectory,
                _extensions?.SkillContributions() ?? []
            )
            {
                Installed = Installed,
            }
        );
        _skills.SetProjectTrusted(await EnsureSkillsTrustedAsync(ct).ConfigureAwait(false));
        _skills.BuildIndex(
            _workspace.WorktreeRoot,
            _extensions?.HookRunner.Options.ContextBudgetPerExtension
                ?? new HookRunnerOptions().ContextBudgetPerExtension,
            message => _extensions?.Log.Warn(message)
        );
        _skillAllowance.Set(_skills.ReadRoots);
        foreach (string warning in _skills.Warnings)
        {
            Notify(warning, NoticeLevel.Warning);
        }

        _skillSection = _skills.Index.Text.Length > 0 ? _skills.Index.Text : null;
        if (_skillSection is { } text)
        {
            RecordSkillsSection(text);
        }

        await ComposeCommandsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The project-skills trust evaluation: trusted and new-worktree decisions pass (the latter
    /// records silently like extension trust), a prompt decision asks the one question listing the
    /// discovered skill names and records on approval. Denial leaves project skills untrusted —
    /// excluded from the index and listed <c>(untrusted)</c>.
    /// </summary>
    private async ValueTask<bool> EnsureSkillsTrustedAsync(CancellationToken ct)
    {
        IReadOnlyList<SkillDescriptor> project =
        [
            .. _skills?.Skills.Where(skill => skill is { Valid: true, Scope: SkillScope.Project })
                ?? [],
        ];
        if (project.Count == 0)
        {
            return true;
        }

        string identity = _workspace.GitCommonDir ?? _workspace.WorktreeRoot;
        ExtensionTrustStore store = TrustStore();
        string hash = SkillTrust.ComputeContentHash(
            project,
            _workspace.RepoRoot ?? _workspace.WorktreeRoot,
            Installed
        );
        switch (store.EvaluateSkills(identity, _workspace.WorktreeRoot, hash))
        {
            case ExtensionTrustDecision.Trusted:
                return true;
            case ExtensionTrustDecision.NewWorktree:
                store.RecordSkillsTrusted(
                    identity,
                    _workspace.WorktreeRoot,
                    hash,
                    DateTimeOffset.UtcNow
                );
                return true;
            default:
                bool approved = await AskTrustAsync(
                        SkillTrust.QuestionTitle,
                        SkillTrust.QuestionLines(project, Installed),
                        ct
                    )
                    .ConfigureAwait(false);
                if (approved)
                {
                    store.RecordSkillsTrusted(
                        identity,
                        _workspace.WorktreeRoot,
                        hash,
                        DateTimeOffset.UtcNow
                    );
                }

                return approved;
        }
    }

    /// <summary>Records the <c>skills</c> section on its first build and on every actual change.</summary>
    private void RecordSkillsSection(string text)
    {
        if (_session is not { } session)
        {
            return;
        }

        SessionPromptSectionEntry? last = session
            .Entries.OfType<SessionPromptSectionEntry>()
            .LastOrDefault(entry =>
                string.Equals(entry.Section, "skills", StringComparison.Ordinal)
            );
        if (last is null || !string.Equals(last.Text, text, StringComparison.Ordinal))
        {
            session.AppendPromptSection("skills", text);
        }
    }

    /// <summary>The <c>/skills</c> notice: the scopes, markers and invalid entries.</summary>
    private string SkillsText() => SkillsView.Text(_skills?.Skills ?? []);

    /// <summary>The <c>/context</c> skills segment.</summary>
    private string SkillsContextSegment()
    {
        SkillIndex.Composed index =
            _skills?.Index ?? new SkillIndex.Composed(string.Empty, 0, 0, []);
        return FormattableString.Invariant(
            $"skills: {index.IndexedCount} (index ~{index.EstimatedTokens} tokens)"
        );
    }

    /// <summary>
    /// <c>/skill:&lt;name&gt; [args]</c>: the wrapped body is submitted through the normal prompt
    /// path and recorded with the skill metadata. Unknown names and untrusted project skills
    /// produce notices instead.
    /// </summary>
    private async Task InvokeSkillAsync(string name, string arguments, CancellationToken ct)
    {
        if (_skills?.TryFind(name, out SkillDescriptor? skill) is not true || skill is null)
        {
            Notify($"unknown skill: {name} (see /skills)");
            return;
        }

        if (skill.Untrusted)
        {
            Notify(SkillTrust.UntrustedNotice(name));
            return;
        }

        if (_turnActive)
        {
            // A skill invocation starts a turn, so it cannot ride the follow-up queue; refusing is
            // the same rule a typed slash command gets while a turn runs.
            Notify(
                "a slash command cannot be queued; wait for the turn to finish and submit it",
                NoticeLevel.Warning
            );
            return;
        }

        await SubmitPromptAsync(
                SkillInvocation.Wrap(skill, arguments),
                new SessionSkillMetadata(skill.Name, skill.Directory),
                ct
            )
            .ConfigureAwait(false);
    }
}
