using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed class ExtensionHostOptions
{
    public required string StorePath { get; init; }

    public IExtensionLog? Log { get; init; }

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
