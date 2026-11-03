using System.Globalization;
using System.Text.Json;

namespace Lunate.Ai;

/// <summary>
/// The provider presets embedded in the build, read once on first use in file order. A preset is
/// the only source of a provider's family URLs and key variable. The embedded file is schema 2:
/// per-family URLs, an optional discovery <c>modelsUrl</c> and declared headers; schema 1 files
/// carry only the <c>baseUrl</c> shorthand. Unknown family names are refused by name.
/// </summary>
public static class ProviderPresets
{
    private const string EmbeddedResourceName = "Lunate.Ai.providers.json";
    private const int OldestSupportedSchemaVersion = 1;
    private const int SupportedSchemaVersion = 2;
    private const string SupportedSchemaVersions = "1 or 2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // The embedded file carries the audit comments next to the values they explain; they
        // are documentation, not data, and are skipped on read.
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
    private static readonly Lazy<PresetTable> Table = new(Load);

    public static IReadOnlyList<ProviderPreset> All => Table.Value.All;

    /// <summary>
    /// Finds a preset by id, ignoring case: a hand-written entry may say <c>OpenAI</c> while the
    /// preset table keeps the canonical <c>openai</c> id for routing and error messages.
    /// </summary>
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

        if (
            file?.SchemaVersion is not int schemaVersion
            || schemaVersion < OldestSupportedSchemaVersion
            || schemaVersion > SupportedSchemaVersion
        )
        {
            string version = file?.SchemaVersion is int value
                ? value.ToString(CultureInfo.InvariantCulture)
                : "missing";
            throw new InvalidDataException(
                $"Unsupported schemaVersion '{version}' in {source}; expected {SupportedSchemaVersions}."
            );
        }

        List<PresetRow>? entries = file.Providers;
        var presets = new List<ProviderPreset>(entries?.Count ?? 0);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (PresetRow row in entries ?? [])
        {
            ProviderPreset preset = ToPreset(row, source);
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

    private static ProviderPreset ToPreset(PresetRow row, string source)
    {
        string id = Require(row.Id, "an id", null, source);
        string name = Require(row.Name, "a name", id, source);
        string adapter = Require(row.Adapter, "an adapter", id, source);
        ProviderAdapter adapterValue = ParseAdapter(adapter, id, source);
        string keyVariable = Require(row.KeyVariable, "a keyVariable", id, source);

        Dictionary<ProviderApiFamily, Uri>? familyUrls = ParseFamilyUrls(row.BaseUrls, id, source);
        Uri? baseUrl = row.BaseUrl is { } baseUrlValue
            ? ParseUrl(baseUrlValue, "'baseUrl'", id, source)
            : null;
        if (familyUrls is { Count: > 0 })
        {
            baseUrl ??= familyUrls.GetValueOrDefault(DefaultFamily(adapterValue));
            if (baseUrl is null)
            {
                throw new InvalidDataException(
                    $"Provider '{id}' in {source} is missing a base URL: declare 'baseUrl' or 'baseUrls' with "
                        + $"'{ProviderApiFamilies.Name(DefaultFamily(adapterValue))}'."
                );
            }
        }

        if (baseUrl is null)
        {
            throw new InvalidDataException($"Provider '{id}' in {source} is missing a base URL.");
        }

        Uri? modelsUrl = row.ModelsUrl is { } modelsUrlValue
            ? ParseUrl(modelsUrlValue, "'modelsUrl'", id, source)
            : null;

        Dictionary<string, string>? headers = ParseHeaders(row.Headers, id, source);

        if (
            !CredentialPlacementFile.TryParseProvider(
                row.CredentialPlacement,
                out Dictionary<ProviderApiFamily, CredentialPlacement>? placements,
                out string? placementError
            )
        )
        {
            throw new InvalidDataException(
                $"Provider '{id}' in {source} declares an invalid credentialPlacement: {placementError}"
            );
        }

        return new ProviderPreset(
            id,
            name,
            adapterValue,
            baseUrl,
            keyVariable,
            string.IsNullOrEmpty(row.KeyVariableAlias) ? null : row.KeyVariableAlias,
            familyUrls,
            modelsUrl,
            headers,
            placements
        );
    }

    private static ProviderApiFamily DefaultFamily(ProviderAdapter adapter) =>
        adapter == ProviderAdapter.Anthropic
            ? ProviderApiFamily.AnthropicMessages
            : ProviderApiFamily.OpenAiChat;

    private static Dictionary<ProviderApiFamily, Uri>? ParseFamilyUrls(
        Dictionary<string, string>? declared,
        string id,
        string source
    )
    {
        if (declared is not { Count: > 0 })
        {
            return null;
        }

        var familyUrls = new Dictionary<ProviderApiFamily, Uri>();
        foreach ((string familyName, string urlValue) in declared)
        {
            if (!ProviderApiFamilies.TryParse(familyName, out ProviderApiFamily family))
            {
                throw new InvalidDataException(
                    $"Provider '{id}' in {source} has an unknown API family '{familyName}' in 'baseUrls'; "
                        + $"supported families: {ProviderApiFamilies.Supported()}."
                );
            }

            Uri url = ParseUrl(urlValue, $"'baseUrls.{familyName}'", id, source);
            familyUrls.Add(family, url);
        }

        return familyUrls;
    }

    private static Uri ParseUrl(string? value, string what, string id, string source)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url))
        {
            throw new InvalidDataException(
                $"Provider '{id}' in {source} declares {what} '{value}', which is not a valid absolute URL."
            );
        }

        return url;
    }

    private static Dictionary<string, string>? ParseHeaders(
        Dictionary<string, string>? declared,
        string id,
        string source
    )
    {
        if (declared is not { Count: > 0 })
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string headerName, string headerValue) in declared)
        {
            if (!ProviderHeaderValues.TryValidate(headerName, headerValue, out string? error))
            {
                throw new InvalidDataException(
                    $"Provider '{id}' in {source} declares invalid headers for '{headerName}': {error}"
                );
            }

            headers.Add(headerName, headerValue);
        }

        return headers;
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
            ById = all.ToDictionary(static preset => preset.Id, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<ProviderPreset> All { get; }

        public Dictionary<string, ProviderPreset> ById { get; }
    }

    private sealed class ProvidersFile
    {
        public int? SchemaVersion { get; set; }

        public List<PresetRow>? Providers { get; set; }
    }

    private sealed class PresetRow
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Adapter { get; set; }

        public string? BaseUrl { get; set; }

        public Dictionary<string, string>? BaseUrls { get; set; }

        public string? ModelsUrl { get; set; }

        public Dictionary<string, string>? Headers { get; set; }

        public JsonElement? CredentialPlacement { get; set; }

        public string? KeyVariable { get; set; }

        public string? KeyVariableAlias { get; set; }
    }
}
