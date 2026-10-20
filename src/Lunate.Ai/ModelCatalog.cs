using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Lunate.Ai;

public sealed class ModelCatalog
{
    private const string EmbeddedResourceName = "Lunate.Ai.models.json";
    private const int OldestSupportedSchemaVersion = 1;

    /// <summary>The schema this build writes (the generator's snapshot and discovery drafts).</summary>
    public const int SupportedSchemaVersion = 2;

    private const string SupportedSchemaVersions = "1 or 2";

    private readonly IReadOnlyList<ModelInfo> _models;
    private readonly Dictionary<string, ModelInfo> _modelsById;

    private ModelCatalog(IReadOnlyList<ModelInfo> models)
    {
        _models = models;
        _modelsById = models.ToDictionary(static model => model.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<ModelInfo> Models => _models;

    public static ModelCatalog Load(string? userFilePath = null)
    {
        return userFilePath is null
            ? LoadLayered(DefaultManagedFilePath(), DefaultUserFilePath())
            : LoadLayered(managedFilePath: null, userFilePath: userFilePath);
    }

    internal static ModelCatalog LoadLayered(string? managedFilePath, string? userFilePath)
    {
        List<ModelInfo> models = ParseEmbedded();
        models = Merge(models, ParseOptional(managedFilePath));
        models = Merge(models, ParseOptional(userFilePath));
        return new ModelCatalog(models);
    }

    /// <summary>
    /// Finds a catalog entry. An exact catalog id wins, so a native id stays the shortest form. The
    /// fallbacks keep typed ids usable: a qualified <c>&lt;provider&gt;/&lt;model&gt;</c> splits on
    /// the first slash and matches the provider plus either the entry's id or its wire id, and a
    /// bare id matches a single entry's wire id. A bare id that more than one preset serves throws,
    /// naming every candidate: the wire call must never guess which provider a person meant. The
    /// first-slash split matters because upstream wire ids contain slashes.
    /// </summary>
    public ModelInfo? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_modelsById.TryGetValue(id, out ModelInfo? exact))
        {
            return exact;
        }

        int slash = id.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0 && slash < id.Length - 1)
        {
            string provider = id[..slash];
            string model = id[(slash + 1)..];
            return Single(
                id,
                [
                    .. _models.Where(candidate =>
                        string.Equals(candidate.Provider, provider, StringComparison.Ordinal)
                        && (
                            string.Equals(candidate.Id, model, StringComparison.Ordinal)
                            || string.Equals(candidate.WireId, model, StringComparison.Ordinal)
                        )
                    ),
                ]
            );
        }

        return Single(
            id,
            [
                .. _models.Where(candidate =>
                    candidate.WireId is not null
                    && string.Equals(candidate.WireId, id, StringComparison.Ordinal)
                ),
            ]
        );
    }

    private static ModelInfo? Single(string id, ModelInfo[] matches) =>
        matches switch
        {
            [] => null,
            [ModelInfo model] => model,
            _ => throw new InvalidOperationException(
                $"Model id '{id}' is ambiguous: {string.Join(", ", matches.Select(model => model.Id))}. "
                    + "Use the qualified id."
            ),
        };

    private static List<ModelInfo> ParseEmbedded()
    {
        using Stream? stream = typeof(ModelCatalog).Assembly.GetManifestResourceStream(
            EmbeddedResourceName
        );
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The embedded resource '{EmbeddedResourceName}' was not found."
            );
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), "built-in models.json");
    }

    private static string DefaultManagedFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lunate",
            "models.catalog.json"
        );

    private static string DefaultUserFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lunate",
            "models.json"
        );

    private static List<ModelInfo> ParseOptional(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return [];
        }

        return Parse(File.ReadAllText(path), path);
    }

    private static List<ModelInfo> Parse(string json, string source)
    {
        CatalogFile? file;
        try
        {
            file = JsonSerializer.Deserialize<CatalogFile>(json, AIJsonUtilities.DefaultOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The model catalog in {source} is malformed: {exception.Message}",
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

        List<ModelEntry> entries = file.Models ?? [];
        var models = new List<ModelInfo>(entries.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ModelEntry entry in entries)
        {
            ModelInfo model = ToModelInfo(entry, source);
            if (!ids.Add(model.Id))
            {
                throw new InvalidDataException($"Duplicate model id '{model.Id}' in {source}.");
            }

            models.Add(model);
        }

        return models;
    }

    private static ModelInfo ToModelInfo(ModelEntry entry, string source)
    {
        if (string.IsNullOrEmpty(entry.Id))
        {
            throw new InvalidDataException($"A model in {source} is missing an id.");
        }

        if (string.IsNullOrEmpty(entry.Provider))
        {
            throw new InvalidDataException(
                $"Model '{entry.Id}' in {source} is missing a provider."
            );
        }

        return new ModelInfo(
            entry.Id,
            entry.Provider,
            entry.Endpoint,
            entry.ContextWindow,
            entry.SupportsTools,
            entry.AuthRef,
            entry.Reasoning
        )
        {
            Pricing = entry.Pricing,
            MaxOutput = entry.MaxOutput,
            SupportsVision = entry.SupportsVision,
            WireId = string.IsNullOrEmpty(entry.WireId) ? null : entry.WireId,
        };
    }

    private static List<ModelInfo> Merge(List<ModelInfo> embedded, List<ModelInfo> user)
    {
        var userById = user.ToDictionary(static model => model.Id, StringComparer.Ordinal);
        var embeddedIds = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<ModelInfo>(embedded.Count + user.Count);
        foreach (ModelInfo model in embedded)
        {
            embeddedIds.Add(model.Id);
            merged.Add(
                userById.TryGetValue(model.Id, out ModelInfo? userModel) ? userModel : model
            );
        }

        merged.AddRange(user.Where(model => !embeddedIds.Contains(model.Id)));
        return merged;
    }

    private sealed class CatalogFile
    {
        public int? SchemaVersion { get; set; }

        public List<ModelEntry>? Models { get; set; }
    }

    private sealed class ModelEntry
    {
        public string? Id { get; set; }

        public string? Provider { get; set; }

        public Uri? Endpoint { get; set; }

        public int ContextWindow { get; set; }

        public bool SupportsTools { get; set; }

        [JsonPropertyName("auth")]
        public string? AuthRef { get; set; }

        public string? Reasoning { get; set; }

        public ModelPricing? Pricing { get; set; }

        public int? MaxOutput { get; set; }

        public bool SupportsVision { get; set; }

        public string? WireId { get; set; }
    }
}
