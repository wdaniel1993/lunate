using System.Globalization;
using System.Text.Json;

namespace Lunate.Ai;

/// <summary>
/// The provider presets embedded in the build, read once on first use in file order. A preset
/// is the only source of a provider's base URL and key variable.
/// </summary>
public static class ProviderPresets
{
    private const string EmbeddedResourceName = "Lunate.Ai.providers.json";
    private const int SupportedSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<PresetTable> Table = new(Load);

    public static IReadOnlyList<ProviderPreset> All => Table.Value.All;

    public static ProviderPreset? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Table.Value.ById.GetValueOrDefault(id);
    }

    internal static IReadOnlyList<ProviderPreset> Parse(string json, string source)
    {
        ProvidersFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ProvidersFile>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The provider presets in {source} are malformed: {exception.Message}",
                exception
            );
        }

        if (file?.SchemaVersion != SupportedSchemaVersion)
        {
            string version = file?.SchemaVersion is int value
                ? value.ToString(CultureInfo.InvariantCulture)
                : "missing";
            throw new InvalidDataException(
                $"Unsupported schemaVersion '{version}' in {source}; expected {SupportedSchemaVersion}."
            );
        }

        List<ProviderEntry> entries = file.Providers ?? [];
        var presets = new List<ProviderPreset>(entries.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProviderEntry entry in entries)
        {
            ProviderPreset preset = ToPreset(entry, source);
            if (!ids.Add(preset.Id))
            {
                throw new InvalidDataException($"Duplicate provider id '{preset.Id}' in {source}.");
            }

            presets.Add(preset);
        }

        return presets;
    }

    private static PresetTable Load()
    {
        using Stream? stream = typeof(ProviderPresets).Assembly.GetManifestResourceStream(
            EmbeddedResourceName
        );
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The embedded resource '{EmbeddedResourceName}' was not found."
            );
        }

        using var reader = new StreamReader(stream);
        return new PresetTable(Parse(reader.ReadToEnd(), "built-in providers.json"));
    }

    private static ProviderPreset ToPreset(ProviderEntry entry, string source)
    {
        string id = Require(entry.Id, "an id", providerId: null, source);
        string name = Require(entry.Name, "a name", id, source);
        string adapter = Require(entry.Adapter, "an adapter", id, source);
        string baseUrlValue = Require(entry.BaseUrl, "a base URL", id, source);
        string keyVariable = Require(entry.KeyVariable, "a keyVariable", id, source);

        if (!Uri.TryCreate(baseUrlValue, UriKind.Absolute, out Uri? baseUrl))
        {
            throw new InvalidDataException(
                $"Provider '{id}' in {source} has an invalid absolute base URL '{baseUrlValue}'."
            );
        }

        return new ProviderPreset(
            id,
            name,
            ParseAdapter(adapter, id, source),
            baseUrl,
            keyVariable,
            string.IsNullOrEmpty(entry.KeyVariableAlias) ? null : entry.KeyVariableAlias
        );
    }

    private static string Require(string? value, string field, string? providerId, string source)
    {
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        string owner = providerId is null
            ? $"A provider in {source}"
            : $"Provider '{providerId}' in {source}";
        throw new InvalidDataException($"{owner} is missing {field}.");
    }

    private static ProviderAdapter ParseAdapter(string adapter, string id, string source) =>
        adapter switch
        {
            "openai" => ProviderAdapter.OpenAi,
            "anthropic" => ProviderAdapter.Anthropic,
            "openai-compatible" => ProviderAdapter.OpenAiCompatible,
            _ => throw new InvalidDataException(
                $"Unknown adapter '{adapter}' for provider '{id}' in {source}."
            ),
        };

    private sealed class PresetTable
    {
        public PresetTable(IReadOnlyList<ProviderPreset> all)
        {
            All = all;
            ById = all.ToDictionary(static preset => preset.Id, StringComparer.Ordinal);
        }

        public IReadOnlyList<ProviderPreset> All { get; }

        public Dictionary<string, ProviderPreset> ById { get; }
    }

    private sealed class ProvidersFile
    {
        public int? SchemaVersion { get; set; }

        public List<ProviderEntry>? Providers { get; set; }
    }

    private sealed class ProviderEntry
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Adapter { get; set; }

        public string? BaseUrl { get; set; }

        public string? KeyVariable { get; set; }

        public string? KeyVariableAlias { get; set; }
    }
}
