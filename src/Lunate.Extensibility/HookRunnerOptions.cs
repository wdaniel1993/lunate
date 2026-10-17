namespace Lunate.Extensibility;

/// <summary>Configuration for <see cref="HookRunner"/>.</summary>
public sealed record HookRunnerOptions
{
    /// <summary>The per-handler timeout for every hook except the provider stream fast path.</summary>
    public TimeSpan HandlerTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The shorter fast-path timeout for <c>ProviderStreamEvent</c> handlers.</summary>
    public TimeSpan ProviderStreamEventTimeout { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>How many continuations one run may request at turn boundaries.</summary>
    public int MaxContinuations { get; init; } = 3;

    /// <summary>
    /// The per-extension context budget in estimated tokens. A token is estimated as one per four
    /// characters of text, rounded up.
    /// </summary>
    public int ContextBudgetPerExtension { get; init; } = 2000;

    /// <summary>
    /// The per-dispatch slow-hook threshold: the first dispatch above it logs one warning per
    /// (extension, hook) and raises <see cref="HookRunner.SlowHook"/>; later crossings only
    /// increment the hook's slow count.
    /// </summary>
    public TimeSpan SlowHookThreshold { get; init; } = TimeSpan.FromMilliseconds(500);
}
