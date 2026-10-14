using Lunate.Ai;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed class ExtensionHostOptions
{
    public required string StorePath { get; init; }

    public IExtensionLog? Log { get; init; }

    /// <summary>The session catalog the model registry falls back to; null disables the fallback.</summary>
    public ModelCatalog? ModelCatalog { get; init; }

    /// <summary>
    /// The client factory the model registry uses for catalog models and declared providers; null
    /// leaves those paths with a teaching error.
    /// </summary>
    public IChatClientFactory? ClientFactory { get; init; }

    public static ExtensionHostOptions Default { get; } =
        new()
        {
            StorePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".lunate"
            ),
        };

    public string ExtensionsPath => Path.Combine(StorePath, "extensions");

    public string SettingsPath => Path.Combine(StorePath, "extensions-settings");

    public string SecretsPath => Path.Combine(StorePath, "extensions-secrets");

    public string TrustFilePath => Path.Combine(StorePath, "trust.json");
}
