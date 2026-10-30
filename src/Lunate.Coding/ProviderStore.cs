using System.Text;
using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>Loads and migrates the user-scoped provider registry.</summary>
internal sealed class ProviderStore
{
    private const int SchemaVersion = 1;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private ProviderStore(IReadOnlyList<ProviderEntry> entries) => Entries = entries;

    public IReadOnlyList<ProviderEntry> Entries { get; }

    public static ProviderStore Load(
        string filePath,
        AuthStore auth,
        IReadOnlyList<CustomProvider> legacyProviders
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(legacyProviders);

        lock (Gate)
        {
            ProviderFile file = Read(filePath);
            var entries = file.Providers.ToDictionary(entry => entry.Name, StringComparer.Ordinal);
            bool migrated = false;

            foreach ((string name, _) in auth.Keys)
            {
                if (entries.ContainsKey(name))
                {
                    continue;
                }

                ProviderEntry? entry = Migrate(name, legacyProviders);
                if (entry is null)
                {
                    continue;
                }

                entries.Add(name, entry);
                migrated = true;
            }

            if (migrated)
            {
                Write(
                    filePath,
                    entries.Values.OrderBy(entry => entry.Name, StringComparer.Ordinal)
                );
            }

            return new ProviderStore(
                entries.Values.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray()
            );
        }
    }

    private static ProviderEntry? Migrate(
        string name,
        IReadOnlyList<CustomProvider> legacyProviders
    )
    {
        ProviderPreset? preset = ProviderPresets.Find(name);
        if (preset is not null)
        {
            return new ProviderEntry(name, preset.Id, preset.BaseUrl, $"auth:{name}");
        }

        CustomProvider? custom = legacyProviders.FirstOrDefault(provider =>
            string.Equals(provider.Id, name, StringComparison.Ordinal)
        );
        return custom is null
            ? null
            : new ProviderEntry(custom.Id, "openai", custom.BaseUrl, $"auth:{name}");
    }

    private static ProviderFile Read(string path)
    {
        if (!File.Exists(path))
        {
            return new ProviderFile(SchemaVersion, []);
        }

        try
        {
            ProviderFile? file = JsonSerializer.Deserialize<ProviderFile>(
                File.ReadAllText(path),
                JsonOptions
            );
            if (file is null || file.SchemaVersion != SchemaVersion || file.Providers is null)
            {
                throw new InvalidDataException(
                    $"Invalid provider store '{path}': expected schemaVersion {SchemaVersion} and a providers array."
                );
            }

            return file;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Invalid provider store '{path}': {exception.Message}",
                exception
            );
        }
    }

    private static void Write(string path, IEnumerable<ProviderEntry> entries)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string targetDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
        string temp = Path.Combine(targetDirectory, Path.GetRandomFileName() + ".tmp");
        string json =
            JsonSerializer.Serialize(
                new ProviderFile(SchemaVersion, entries.ToArray()),
                JsonOptions
            ) + "\n";
        try
        {
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private sealed record ProviderFile(int SchemaVersion, ProviderEntry[] Providers);
}
