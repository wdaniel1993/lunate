using Lunate.Extensibility;
using Lunate.Tui;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private InstalledPackages? _installed;

    /// <summary>The key sequence at which the pending trust question armed (test seam).</summary>
    internal long TrustArmSequence => Interlocked.Read(ref _trustArmSequence);

    /// <summary>
    /// Asks one trust question in the live area; true approves. The model carries the precomputed
    /// position (<c>n of m</c>), and the key sequence recorded after the question renders is the
    /// type-ahead arm: only keys read after it can decide.
    /// </summary>
    private async ValueTask<bool> AskTrustAsync(
        string title,
        IReadOnlyList<string> details,
        CancellationToken ct
    )
    {
        int step = ++_trustAsked;
        var model = new TrustPromptModel(title, details, step, Math.Max(_trustTotal, step));
        var decision = new TaskCompletionSource<TrustChoice>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _trustPrompt = model;
        _trustDecision = decision;
        _live.SetTrustPrompt(model);
        Interlocked.Exchange(ref _trustArmSequence, Interlocked.Read(ref _keySequence));
        using CancellationTokenRegistration registration = ct.Register(() =>
            decision.TrySetCanceled(ct)
        );
        TrustChoice choice;
        try
        {
            choice = await decision.Task.ConfigureAwait(false);
        }
        finally
        {
            _trustPrompt = null;
            _trustDecision = null;
            _live.SetTrustPrompt(null);
        }

        if (choice is TrustChoice.Approve)
        {
            Notify("the decision is remembered for this repository — change it with /trust");
        }
        else
        {
            Notify(
                "not trusted — nothing was recorded, so the next load asks again; /trust lists this repository's records"
            );
        }

        return choice is TrustChoice.Approve;
    }

    private void ResolveTrust(TaskCompletionSource<TrustChoice> pending, TrustChoice choice)
    {
        _trustPrompt = null;
        _trustDecision = null;
        _live.SetTrustPrompt(null);
        pending.TrySetResult(choice);
    }

    /// <summary>
    /// The number of trust questions this session will ask, from the shared inventory: project
    /// extensions whose trust evaluates to <c>Prompt</c> (discovery and content hash only, no
    /// assembly load), plus one per untrusted project <c>mcp.json</c> file and one per unapproved
    /// project-scoped server definition. The count labels each question <c>n of m</c>.
    /// </summary>
    private int CountPendingTrustQuestions() =>
        TrustInventory.Create(_workspace, _options.Extensions).PendingCount;

    /// <summary>
    /// <c>/trust</c> lists this repository's records (extension trust and mcp.json, short hashes);
    /// <c>/trust clear</c> removes them, so the next load asks again.
    /// </summary>
    private void HandleTrustCommand(string argument)
    {
        string identity = _workspace.GitCommonDir ?? _workspace.WorktreeRoot;
        ExtensionTrustStore store = TrustStore();
        if (argument.Length == 0)
        {
            Notify(TrustFormat.RecordsText(store.Records(identity)));
            return;
        }

        if (string.Equals(argument, "clear", StringComparison.Ordinal))
        {
            store.ClearRepository(identity, _workspace.WorktreeRoot);
            Notify("trust records cleared for this repository — the next load asks again");
            return;
        }

        Notify("usage: /trust [clear]");
    }

    private ExtensionTrustStore TrustStore() => new(TrustOptions());

    /// <summary>
    /// What this machine installed, read once per session: the trust digests bind the package a
    /// directory came from, and <c>/skills</c> names it (design D7).
    /// </summary>
    private InstalledPackages Installed =>
        _installed ??= InstalledPackages.Read(TrustOptions().StorePath);

    private ExtensionHostOptions TrustOptions() =>
        new()
        {
            StorePath = _options.Extensions?.StorePath ?? ExtensionHostOptions.Default.StorePath,
        };
}
