using Lunate.Ai;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed class ExtensionHostOptions
{
    public required string StorePath { get; init; }

    public IExtensionLog? Log { get; init; }

    /// <summary>
    /// The one-line user notice sink (a remote extension crash); null logs only.
    /// </summary>
    public Action<string>? Notify { get; init; }

    /// <summary>The session catalog the model registry falls back to; null disables the fallback.</summary>
    public ModelCatalog? ModelCatalog { get; init; }

    /// <summary>
    /// The client factory the model registry uses for catalog models and declared providers; null
    /// leaves those paths with a teaching error.
    /// </summary>
    public IChatClientFactory? ClientFactory { get; init; }

    /// <summary>
    /// The bare command names the core owns. An extension command with one of these names refuses
    /// the load; the production composition sets this from the built-in command table.
    /// </summary>
    public IReadOnlyList<string> ReservedCommands { get; init; } = [];

    /// <summary>The core model-facing tool names extension aliases may not shadow.</summary>
    internal IReadOnlyList<string> ReservedToolNames { get; init; } = [];

    /// <summary>
    /// The extensions the host composition supplies (manifest plus factory), in composition order;
    /// discovery folds them in before the store scans. Empty by default.
    /// </summary>
    public IReadOnlyList<BuiltInExtension> BuiltIns { get; init; } = [];

    /// <summary>
    /// True composes only the built-in set (the <c>lunate mcp roslyn</c> composition): discovery
    /// skips the store scans and the single-file candidates, so no third-party extension is
    /// discovered, trusted or loaded. Built-ins from <see cref="BuiltIns"/> still load. The
    /// <c>--no-extensions</c> composition uses this with an empty built-in list.
    /// </summary>
    public bool BuiltInsOnly { get; init; }

    /// <summary>
    /// The ceiling the host puts over every workspace service's loaded-workspace cap: the manifest
    /// may declare any positive cap, the host may lower it, and it never raises it. Null leaves the
    /// manifest's declared cap (default 2) the effective one.
    /// </summary>
    public int? WorkspaceServiceCapLimit { get; init; }

    /// <summary>
    /// Where locked packages are read from — the compile and the load must agree, or a build would
    /// compile against files the load cannot find. Null means NuGet's own folder
    /// (<c>NUGET_PACKAGES</c>, else <c>~/.nuget/packages</c>); a test or a portable install sets it
    /// explicitly.
    /// </summary>
    public string? GlobalPackagesFolder { get; init; }

    public static ExtensionHostOptions Default { get; } =
        new() { StorePath = Lunate.Ai.LunateHome.Store };

    public string ExtensionsPath => Path.Combine(StorePath, "extensions");

    public string SettingsPath => Path.Combine(StorePath, "extensions-settings");

    public string SecretsPath => Path.Combine(StorePath, "extensions-secrets");

    public string TrustFilePath => Path.Combine(StorePath, "trust.json");
}
