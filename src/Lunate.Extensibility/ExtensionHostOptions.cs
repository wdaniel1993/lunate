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
