using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed class ExtensionSecretsStore : IExtensionSecrets
{
    private readonly JsonElement _root;

    private ExtensionSecretsStore(JsonElement root) => _root = root;

    public static ExtensionSecretsStore Load(ExtensionHostOptions options, string extensionId)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        string path = Path.Combine(options.SecretsPath, extensionId + ".json");
        return new ExtensionSecretsStore(
            JsonFiles.ReadObject(path, $"extension '{extensionId}': secrets")
        );
    }

    public bool TryGet(string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (
            !_root.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.String
        )
        {
            return false;
        }

        value = element.GetString()!;
        return true;
    }
}
