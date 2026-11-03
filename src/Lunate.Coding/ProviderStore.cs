using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    public static void Save(string filePath, IReadOnlyList<ProviderEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(entries);

        lock (Gate)
        {
            var validated = new List<ProviderEntry>(entries.Count);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProviderEntry entry in entries)
            {
                if (
                    !ProviderEntry.TryCreate(
                        entry.Name,
                        entry.Kind,
                        entry.BaseUrl,
                        entry.CredentialReference,
                        entry.Headers,
                        entry.Region,
                        entry.BaseUrls,
                        entry.ModelsUrl,
                        entry.CredentialPlacements,
                        out ProviderEntry? safeEntry,
                        out string? error
                    ) || safeEntry is null
                )
                {
                    throw new InvalidDataException(
                        $"Invalid provider entry '{entry.Name}': {error}. No provider settings were written."
                    );
                }

                if (!names.Add(safeEntry.Name))
                {
                    throw new InvalidDataException(
                        $"Duplicate provider name '{safeEntry.Name}'. No provider settings were written."
                    );
                }

                validated.Add(safeEntry);
            }

            Write(filePath, validated.OrderBy(entry => entry.Name, StringComparer.Ordinal));
        }
    }

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
            var entries = new Dictionary<string, ProviderEntry>(StringComparer.Ordinal);
            foreach (ProviderFileRecord record in file.Providers)
            {
                // Read validated every row already, so this conversion cannot fail.
                TryCreateRecord(record, out ProviderEntry? entry, out _);
                entries.Add(entry!.Name, entry);
            }

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

            var entries = new List<ProviderFileRecord>(file.Providers.Length);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProviderFileRecord record in file.Providers)
            {
                if (
                    !TryCreateRecord(record, out ProviderEntry? entry, out string? error)
                    || entry is null
                )
                {
                    throw new InvalidDataException(
                        $"Invalid provider store '{path}': {error}. No providers were loaded."
                    );
                }

                if (!names.Add(entry.Name))
                {
                    throw new InvalidDataException(
                        $"Invalid provider store '{path}': duplicate provider name '{entry.Name}'. No providers were loaded."
                    );
                }

                entries.Add(ToFileRecord(entry));
            }

            return file with
            {
                Providers = entries.ToArray(),
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Invalid provider store '{path}': {exception.Message}",
                exception
            );
        }
    }

    /// <summary>
    /// Maps a file record to a validated entry. <c>baseUrls</c> keys must name a known API
    /// family — an unknown key is refused by name with the supported set — and every declared
    /// URL passes the provider entry's endpoint rule.
    /// </summary>
    private static bool TryCreateRecord(
        ProviderFileRecord record,
        out ProviderEntry? entry,
        out string? error
    )
    {
        entry = null;
        Dictionary<ProviderApiFamily, Uri>? baseUrls = null;
        if (record.BaseUrls is { Count: > 0 } declared)
        {
            baseUrls = [];
            foreach ((string familyName, string urlValue) in declared)
            {
                if (!ProviderApiFamilies.TryParse(familyName, out ProviderApiFamily family))
                {
                    error =
                        $"providers.{record.Name}.baseUrls has an unknown API family '{familyName}'; "
                        + $"supported families: {ProviderApiFamilies.Supported()}.";
                    return false;
                }

                if (!Uri.TryCreate(urlValue, UriKind.Absolute, out Uri? url))
                {
                    error =
                        $"providers.{record.Name}.baseUrls.{familyName} is '{urlValue}', which is not a valid absolute URL.";
                    return false;
                }

                baseUrls.Add(family, url);
            }
        }

        if (
            !CredentialPlacementFile.TryParseProvider(
                record.CredentialPlacement,
                out Dictionary<ProviderApiFamily, CredentialPlacement>? placements,
                out string? placementError
            )
        )
        {
            error = $"providers.{record.Name}.credentialPlacement {placementError}";
            return false;
        }

        return ProviderEntry.TryCreate(
            record.Name,
            record.Kind,
            record.BaseUrl,
            record.CredentialReference,
            record.Headers,
            record.Region,
            baseUrls,
            record.ModelsUrl,
            placements,
            out entry,
            out error
        );
    }

    private static ProviderFileRecord ToFileRecord(ProviderEntry entry) =>
        new(
            entry.Name,
            entry.Kind,
            entry.BaseUrl,
            entry.CredentialReference,
            entry.Headers,
            entry.Region,
            entry.BaseUrls is { Count: > 0 } baseUrls
                ? baseUrls.ToDictionary(
                    static family => ProviderApiFamilies.Name(family.Key),
                    static family => family.Value.OriginalString,
                    StringComparer.Ordinal
                )
                : null,
            entry.ModelsUrl,
            ToFilePlacement(entry.CredentialPlacements)
        );

    /// <summary>
    /// The file declaration is always family-keyed: a provider-wide object would mean "every
    /// family" on read, and a one-family map written that way would silently widen on reload.
    /// </summary>
    private static JsonElement? ToFilePlacement(
        IReadOnlyDictionary<ProviderApiFamily, CredentialPlacement>? placements
    )
    {
        if (placements is not { Count: > 0 })
        {
            return null;
        }

        var keyed = new JsonObject();
        foreach ((ProviderApiFamily family, CredentialPlacement placement) in placements)
        {
            // A value that survives the compact round-trip writes as a compact string; a scheme
            // needs the object's own field.
            string? compact = placement.ToCompact();
            bool compactRoundTrips =
                compact is not null
                && CredentialPlacement.TryParseCompact(
                    compact,
                    out CredentialPlacement? reparsed,
                    out _
                )
                && reparsed == placement;
            keyed[ProviderApiFamilies.Name(family)] = compactRoundTrips
                ? compact
                : PlacementNode(placement);
        }

        return ToElement(keyed);
    }

    /// <summary>A null result is "no declaration": the clone needs no element.</summary>
    private static JsonElement? ToElement(JsonObject node)
    {
        JsonElement element = JsonSerializer.SerializeToElement(node, JsonOptions);
        return element;
    }

    private static JsonObject PlacementNode(CredentialPlacement placement)
    {
        var form = new JsonObject
        {
            ["in"] = placement.Target == CredentialPlacementTarget.Query ? "query" : "header",
            ["name"] = placement.Name,
        };
        if (
            placement.Target == CredentialPlacementTarget.Header
            && !string.IsNullOrEmpty(placement.Scheme)
        )
        {
            form["scheme"] = placement.Scheme;
        }

        return form;
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
                new ProviderFile(SchemaVersion, entries.Select(ToFileRecord).ToArray()),
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

    private sealed record ProviderFile(int SchemaVersion, ProviderFileRecord[] Providers);

    private sealed record ProviderFileRecord(
        string? Name,
        string? Kind,
        Uri? BaseUrl,
        string? CredentialReference,
        IReadOnlyDictionary<string, string>? Headers,
        string? Region,
        IReadOnlyDictionary<string, string>? BaseUrls,
        Uri? ModelsUrl,
        JsonElement? CredentialPlacement = null
    );
}
