using Lunate.Ai;
using Lunate.Tui;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace Lunate.Coding;

// The setup wizard's summary, commit and the interaction primitives the steps drive (design §5,
// §8): the summary shows every collected value and every skipped step and writes nothing before it
// is confirmed with Enter; the commit is the one pass through the store writers; the reconciliation
// reloads the composition the way the stores now read.

internal sealed partial class InteractiveSession
{
    /// <summary>Step 9: every collected value — provider, region, credential source, endpoint,
    /// family, placement, models URL, test results, model, profiles, decision kind, decisions —
    /// and every skipped step, one per line.</summary>
    private void RenderSummary(SetupWizardModel wizard)
    {
        List<string> lines = ["setup — confirm these values (nothing has been written yet):"];
        lines.AddRange(SetupWizardSummary.Render(wizard));

        _live.WriteScrollback(console =>
        {
            foreach (string line in lines)
            {
                console.Write(new Markup(Markup.Escape(line + "\n")));
            }
        });
    }

    private async Task<bool> ConfirmAsync(string question, CancellationToken ct)
    {
        string? answered = await PickAsync(
                question,
                ["Write the configuration", "Cancel"],
                ["write", "cancel"],
                PickerKind.WizardChoice,
                ct,
                "write"
            )
            .ConfigureAwait(false);
        return answered == "write";
    }

    private void CommitWizard(SetupWizardModel wizard)
    {
        string settingsPath = _options.SettingsPath ?? AuthStore.SettingsFilePath(null);
        string providerPath = AuthStore.ProviderStoreFilePath(_options.AuthPath);
        SetupWizardCommit.Commit(
            wizard,
            new SetupWizardCommit.CommitPaths(settingsPath, providerPath),
            () => _auth,
            () => _providerEntries
        );
        Notify("setup written: the provider, credential and profile stores are updated");
    }

    /// <summary>
    /// After a confirmed commit the composition follows the stores: the settings, provider
    /// entries, catalog and credential store reload, and the chat profile switches the running
    /// session onto the configured model like a <c>/model</c> choice did.
    /// </summary>
    private async Task ReconcileAsync(SetupWizardModel wizard, CancellationToken ct)
    {
        string settingsPath = _options.SettingsPath ?? AuthStore.SettingsFilePath(null);
        _auth = AuthStore.Load(_options.AuthPath, _options.Environment);
        ProviderStore userProviders = ProviderStore.Load(
            AuthStore.ProviderStoreFilePath(_options.AuthPath),
            _auth,
            []
        );
        _providerEntries = userProviders.Entries;
        _settings = SettingsStore.Resolve(settingsPath, _options.Environment);
        _settings = SettingsStore.ApplyLaunch(_settings, _options.Launch);
        _catalog = HarnessFactory.LoadCatalog(_settings, _options.ModelsPath);

        // The chat profile a run staged: the full walk's profile (which also bound the usage),
        // and the entry menu's own answer — "change the chat profile" rebinds the usage alone.
        string? chatName =
            wizard.ChatProfile is { } chat && wizard.BindChatUsage
                ? chat.Name
                : wizard.ChatUsageBinding;
        if (chatName is { } bind)
        {
            try
            {
                ResolvedModelProfile resolved = ModelSelectionResolver.ResolveChatModel(
                    bind,
                    _settings,
                    _providerEntries,
                    _catalog
                );
                if (!_providerEntries.Any(entry => entry.Name == resolved.Provider.Name))
                {
                    _providerEntries = [.. _providerEntries, resolved.Provider];
                }

                _settings = ModelSelectionResolver.SelectChatProfile(_settings, resolved);
                await SwitchToModel(resolved.Model, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception exception)
                when (exception
                        is UnknownModelException
                            or InvalidDataException
                            or InvalidOperationException
                            or ArgumentException
                )
            {
                Notify(
                    $"setup committed the stores; the chat profile did not resolve here: {exception.Message}",
                    NoticeLevel.Warning
                );
            }
        }

        Notify("setup written — the walk's fixed answer waits for the next session");
    }

    // --- the primitives ---

    private async Task<string?> PickAsync(
        string titlePrefix,
        List<string> labels,
        List<string> values,
        PickerKind kind,
        CancellationToken ct,
        string? selected = null
    )
    {
        var pending = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _wizardPick = pending;
        using CancellationTokenRegistration registration = ct.Register(() =>
            pending.TrySetCanceled(ct)
        );
        int current = values.IndexOf(selected ?? string.Empty);
        OpenPicker(kind, titlePrefix, labels, values, current < 0 ? 0 : current);
        try
        {
            string? picked = await pending.Task.ConfigureAwait(false);
            return picked == WizardAbortValue ? null : picked;
        }
        finally
        {
            _wizardPick = null;
        }
    }

    private Task<string?> PromptAsync(string label, CancellationToken ct) =>
        _secretPrompt.ReadTextAsync(label, ct);

    /// <summary>The walk's one masked read: the credential key typed at the credential step.</summary>
    private Task<string?> SecretPromptAsync(string label, CancellationToken ct) =>
        _secretPrompt.ReadAsync(label, ct);

    /// <summary>One decision switch through the question component: Enter picks the selected row,
    /// Esc aborts (the walk's abort, never the session's). The selection lands on the default.</summary>
    private async Task<string?> AskQuestionAsync(
        string header,
        string question,
        IReadOnlyList<string> options,
        CancellationToken ct
    )
    {
        SelectListQuestionModel model = SelectListQuestionModel.Create(
            header,
            question,
            [.. options.Select(option => new SelectListQuestionOption(option, string.Empty))],
            multiSelect: false,
            null
        );
        var interaction = new SelectListQuestionInteraction(_scheduler);
        interaction.BeginQuestion(model, Interlocked.Read(ref _keySequence));
        var decision = new TaskCompletionSource<SelectListQuestionModel>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _questionPrompt = model;
        _questionInteraction = interaction;
        _questionDecision = decision;
        _live.SetQuestion(model);
        using CancellationTokenRegistration registration = ct.Register(() =>
            decision.TrySetCanceled(ct)
        );
        try
        {
            SelectListQuestionModel answered = await decision.Task.ConfigureAwait(false);
            if (answered.Status != QuestionPromptStatus.Submitted)
            {
                return WizardAbortValue;
            }

            return answered.Answer?.SelectedOptions.FirstOrDefault() ?? WizardAbortValue;
        }
        finally
        {
            _questionPrompt = null;
            _questionInteraction = null;
            _questionDecision = null;
            _live.SetQuestion(null);
        }
    }
}

/// <summary>
/// The summary's lines: one value per line, group headers included, so the terminal note and the
/// fixture pins read the same content at every width (layout is pinned at 80/60 columns by the
/// fixture tests; the content is what matters here).
/// </summary>
internal static class SetupWizardSummary
{
    internal static IReadOnlyList<string> Render(SetupWizardModel wizard)
    {
        List<string> lines = [];
        if (wizard.Provider is { } provider)
        {
            lines.Add(
                $"provider: {provider.Id}"
                    + (
                        provider.Custom
                            ? $" (custom endpoint {provider.Endpoint} — family {provider.ApiFamily}, placement {provider.Placement.ToCompact()}, region {provider.Region ?? "(none)"}"
                                + (
                                    provider.ModelsUrl is { } modelsUrl
                                        ? $", models URL {modelsUrl}"
                                        : ""
                                )
                                + ")"
                        : provider.Region is { } region ? $" (region {region})"
                        : string.Empty
                    )
            );
        }

        if (wizard.Credential is { } credential)
        {
            lines.Add(
                "credential: "
                    + (
                        credential.Key is not null
                            ? "the key you typed (stored only in the auth store)"
                            : credential.Reference
                    )
            );
        }

        switch (wizard.ConnectionTest?.Outcome)
        {
            case WizardProgress.Passed:
                lines.Add("connection test: passed");
                break;
            case WizardProgress.Skipped:
                lines.Add("connection test: skipped");
                break;
            case WizardProgress.Failed:
                lines.Add($"connection test: failed ({wizard.ConnectionTest?.Detail})");
                break;
        }

        if (wizard.Model is { } model)
        {
            lines.Add($"model: {model.ModelId} (family {model.ApiFamily})");
        }

        switch (wizard.ModelCheck?.Outcome)
        {
            case WizardProgress.Passed:
                lines.Add("model check: passed");
                break;
            case WizardProgress.Skipped:
                lines.Add("model check: skipped — the model stays unverified");
                break;
            case WizardProgress.Failed:
                lines.Add(
                    $"model check: failed ({wizard.ModelCheck?.Detail}) — the model stays unverified"
                );
                break;
        }

        if (wizard.ChatProfile is { } chat)
        {
            lines.Add(
                "chat profile: "
                    + chat.Name
                    + (chat.ReasoningLevel is { } level ? $" (thinking {level})" : string.Empty)
            );
        }

        if (wizard.ChatUsageBinding is { } bound)
        {
            lines.Add("chat usage: " + bound);
        }

        if (wizard.FastProfile && wizard.ChatProfile is { } fastProfile)
        {
            lines.Add($"fast usage: {fastProfile.Name}");
        }

        if (wizard.DecisionProfile is { } decision)
        {
            lines.Add(
                "decision profile: "
                    + decision.ProfileName
                    + (decision.Native ? " (native" : " (adapter")
                    + (decision.PinnedVersion is { } pinned ? $", pinned {pinned}" : string.Empty)
                    + ")"
            );
        }

        if (wizard.DecisionProfile is not null)
        {
            switch (wizard.DecisionCheck?.Outcome)
            {
                case WizardProgress.Passed:
                    lines.Add("decision check: passed");
                    break;
                case WizardProgress.Skipped:
                    lines.Add("decision check: skipped — the decision profile stays unverified");
                    break;
                case WizardProgress.Failed:
                    lines.Add(
                        $"decision check: failed ({wizard.DecisionCheck?.Detail}) — the decision "
                            + "profile stays unverified"
                    );
                    break;
                default:
                    lines.Add("decision check: not run — the decision profile stays unverified");
                    break;
            }
        }

        if (wizard.Decisions is { } decisions)
        {
            if (decisions.Approvals is { } approvals)
            {
                lines.Add($"decisions.approvals: {approvals}");
            }

            if (decisions.Routing is { } routing)
            {
                lines.Add(
                    $"decisions.routing: {routing} (routing profiles: {string.Join(", ", RoutingCandidateNames(wizard))})"
                );
            }

            if (decisions.Tool is { } tool)
            {
                lines.Add($"decisions.tool: {tool}");
            }

            if (decisions.Questions is { } questions)
            {
                lines.Add($"decisions.questions: {questions}");
            }

            foreach ((string profile, string description) in decisions.RoutingDescriptions)
            {
                lines.Add($"routing description ({profile}): {description}");
            }
        }
        else if (wizard.IsSkipped(SetupStep.Decisions))
        {
            lines.Add("decisions: defaults accepted (nothing written for the layer)");
        }

        if (wizard.SkippedSteps is { Count: > 0 } skipped)
        {
            lines.Add(
                $"skipped: "
                    + string.Join(
                        ", ",
                        skipped.Select(step =>
                            step switch
                            {
                                SetupStep.Provider => "provider",
                                SetupStep.Credential => "credential",
                                SetupStep.ConnectionTest => "connection test",
                                SetupStep.Model => "model",
                                SetupStep.ModelCheck => "model check",
                                SetupStep.ChatProfile => "chat profile",
                                SetupStep.FastProfile => "fast usage",
                                SetupStep.DecisionProfile => "decision profile",
                                SetupStep.Decisions => "decisions settings",
                                _ => step.ToString(),
                            }
                        )
                    )
            );
        }

        return lines;
    }

    private static IReadOnlyList<string> RoutingCandidateNames(SetupWizardModel wizard)
    {
        var names = new List<string>();
        if (wizard.ChatProfile is { } chat)
        {
            names.Add(chat.Name);
        }

        if (wizard.DecisionProfile is { } decision)
        {
            names.Add(decision.ProfileName);
        }

        foreach (string existing in wizard.ExistingRoutingProfiles)
        {
            if (!names.Contains(existing, StringComparer.Ordinal))
            {
                names.Add(existing);
            }
        }

        return names;
    }
}

file static class WizardCredentialSourceExtensions
{
    public static string Source(this WizardCredentialChoice credential, string reference) =>
        credential.Key is null ? reference : "the key you typed (stored only in the auth store)";
}
