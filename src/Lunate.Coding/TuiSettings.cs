namespace Lunate.Coding;

/// <summary>
/// The TUI presentation settings (the <c>tui</c> section of the settings file): the named theme
/// and the persisted key remaps. The theme literal is exact lowercase; every remap entry is
/// <c>binding id → [{key spec}]</c>, validated against the catalogue by
/// <see cref="SettingsStore"/> on read and on write. A remap is the binding's declared keys —
/// the catalogue never gains a second declaration.
/// </summary>
public sealed record TuiSettings
{
    /// <summary>An empty remap table, the record's default.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoKeys =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Whether the interactive session runs on the alternate screen; <c>--fullscreen</c> turns it
    /// on for one run. Inline rendering stays the default (the issue's rule: opt-in only).
    /// </summary>
    public bool Fullscreen { get; init; }

    /// <summary>The named theme; <c>default</c> keeps the terminal's detected colour system.</summary>
    public string Theme { get; init; } = "default";

    /// <summary>The persisted remaps: binding id to its declared keys, each in the help spelling.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Keys { get; init; } = NoKeys;
}
