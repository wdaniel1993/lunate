using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// What one <c>/extensions reload</c> produced: whether the swap happened, the build it ran (with the
/// reason it stopped, when it did), the key now loaded, and what the drain did to the old build's live
/// invocations. <see cref="Build"/> is always set, so a failed reload can render the same row a build
/// failure does.
/// </summary>
public sealed record SingleFileReloadOutcome
{
    public required string Id { get; init; }

    /// <summary>True when the new build is loaded; false when the old one was left in place.</summary>
    public required bool Reloaded { get; init; }

    public required SingleFileBuildOutcome Build { get; init; }

    /// <summary>The build key now loaded (unchanged from before when nothing was swapped).</summary>
    public string? LoadedBuildKey { get; init; }

    /// <summary>The one-line reason a reload did not happen; null on success.</summary>
    public string? Reason { get; init; }

    /// <summary>The command ids the reload cancelled, so the caller can say what it interrupted.</summary>
    public IReadOnlyList<string> CancelledCommands { get; init; } = [];

    /// <summary>False when a cancelled handler ignored its token and outlived the drain bound.</summary>
    public bool Drained { get; init; } = true;
}

public sealed partial class ExtensionLoader
{
    /// <summary>
    /// How long a reload waits for the old build's live invocations to leave before unloading anyway.
    /// Bounded rather than unlimited: a handler that ignores its token must not hold a session open,
    /// and the drain is what makes "the context disposes only after its invocations ended" true for
    /// every handler that does honour it.
    /// </summary>
    public static TimeSpan ReloadDrainBound { get; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Reloads one single-file extension in the order the feature exists for: the file's current
    /// bytes are trusted and built <b>first</b>, against the lock bound to those bytes, and only a
    /// successful build touches what is running. A trust or build failure returns with the old build
    /// still loaded and callable — the pointer in <c>builds.json</c> is not moved by a failed
    /// compile, so the extension a model is talking to is never the one that does not exist.
    /// <para>
    /// The swap then cancels the old build's live invocations (one notice naming the extension),
    /// drains them within <see cref="ReloadDrainBound"/>, unloads the old context, loads the new
    /// build, and fires <c>ToolsChanged</c> once, after the new tool set is registered, so the list a
    /// model sees is never half-swapped.
    /// </para>
    /// </summary>
    public async Task<SingleFileReloadOutcome> ReloadSingleFileAsync(
        string id,
        SingleFileExtensionBuilder builder,
        IExtensionTrustPrompt? prompt = null,
        CancellationToken cancellationToken = default,
        bool startSession = true
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(builder);

        SingleFileExtension? candidate = _singleFile.FirstOrDefault(extension =>
            string.Equals(extension.Id, id, StringComparison.Ordinal)
        );
        if (candidate is null)
        {
            return new SingleFileReloadOutcome
            {
                Id = id,
                Reloaded = false,
                Build = EmptyBuild(id, "not discovered"),
                Reason =
                    DiscoveredSingleFileIds() == "none"
                        ? $"'{id}' is not a discovered single-file extension; there are none."
                        : $"'{id}' is not a discovered single-file extension; discovered: {DiscoveredSingleFileIds()}.",
            };
        }

        // Preflight: trust, then the lock bound to those bytes, then the cache, then the compiler.
        // Nothing below this line runs when any of the four refuses.
        SingleFileBuildOutcome built = await builder
            .BuildAsync(candidate.Path, candidate.Scope, prompt, cancellationToken)
            .ConfigureAwait(false);
        if (!built.Succeeded)
        {
            return new SingleFileReloadOutcome
            {
                Id = id,
                Reloaded = false,
                Build = built,
                LoadedBuildKey = LoadedSingleFileKey(id),
                Reason =
                    built.Reason
                    ?? built.Extension.Reason
                    ?? "the build did not produce an assembly",
            };
        }

        bool wasLoaded = _loaded.ContainsKey(id);
        CommandDrain drain = wasLoaded
            ? await _commandSources
                .DrainForReloadAsync(id, ReloadDrainBound, cancellationToken)
                .ConfigureAwait(false)
            : CommandDrain.None;
        if (wasLoaded)
        {
            // ToolsChanged is suppressed here: the new build is not registered yet, and the fire
            // belongs after it is.
            await Unload(id, notifyToolsChanged: false).ConfigureAwait(false);
        }

        try
        {
            await LoadSingleFileAsync(
                    id,
                    cancellationToken,
                    startSession,
                    notifyToolsChanged: false
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SingleFileReloadOutcome
            {
                Id = id,
                Reloaded = false,
                Build = built,
                Reason = FormattableString.Invariant(
                    $"the new build could not be loaded: {exception.Message}"
                ),
                CancelledCommands = drain.Cancelled,
                Drained = drain.Drained,
            };
        }

        // The swap is complete and the registry holds the new build's tools; one fire, now. Inspect
        // mode (the CLI's verifying reload) dispatches nothing, like every other inspect load.
        if (startSession)
        {
            await NotifyToolsChangedAsync().ConfigureAwait(false);
        }

        return new SingleFileReloadOutcome
        {
            Id = id,
            Reloaded = true,
            Build = built,
            LoadedBuildKey = built.Build!.Key,
            CancelledCommands = drain.Cancelled,
            Drained = drain.Drained,
        };
    }

    /// <summary>The build key currently loaded for <paramref name="id"/>, or null when it is not loaded.</summary>
    public string? LoadedSingleFileKey(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _singleFileKeys.TryGetValue(id, out string? key) ? key : null;
    }

    /// <summary>The outcome a reload reports before it has a build to talk about (an unknown id).</summary>
    private static SingleFileBuildOutcome EmptyBuild(string id, string reason) =>
        new()
        {
            Status = SingleFileBuildStatus.Invalid,
            Extension = new SingleFileExtension
            {
                Path = id,
                Scope = ExtensionScope.Global,
                Header = ExtensionHeader.None,
                State = SingleFileExtensionState.Invalid,
                Reason = reason,
            },
            Reason = reason,
        };
}
