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

    /// <summary>
    /// Loads the embedded catalog and layers the managed cache and the user file over it.
    /// A schema-2 layer replaces an entry completely; a schema-1 layer can only express routing and
    /// token limits, so an override keeps the lower entry's <see cref="ModelInfo.Pricing"/>,
    /// <see cref="ModelInfo.MaxOutput"/>, <see cref="ModelInfo.SupportsVision"/> and
    /// <see cref="ModelInfo.WireId"/>. A schema-1 entry without a lower entry has no such metadata.
    /// </summary>
    public static ModelCatalog Load(string? userFilePath = null)
    {
        return userFilePath is null
            ? LoadLayered(DefaultManagedFilePath(), DefaultUserFilePath())
            : LoadLayered(managedFilePath: null, userFilePath: userFilePath);
    }

    internal static ModelCatalog LoadLayered(string? managedFilePath, string? userFilePath)
    {
        List<ModelInfo> models = ParseEmbedded().Models;
        models = Merge(models, ParseOptional(managedFilePath));
        models = Merge(models, ParseOptional(userFilePath));
        return new ModelCatalog(models);
    }

    /// <summary>
    /// Finds a catalog entry. An exact catalog id wins, so a native id stays the shortest form. The
    /// fallbacks keep typed ids usable: a qualified <c>&lt;provider&gt;/&lt;model&gt;</c> splits on
    /// the first slash and matches the provider plus the entry's wire id (or its id when it has no
    /// wire id), and a bare id matches a single entry's wire id. A bare id that more than one preset
    /// serves throws, naming every candidate: the wire call must never guess which provider a
    /// person meant. The first-slash split matters because upstream wire ids contain slashes.
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
                            string.Equals(candidate.WireId, model, StringComparison.Ordinal)
                            // A native or hand-written entry has no wire id: its id is what goes on the wire.
                            || (
                                candidate.WireId is null
                                && string.Equals(candidate.Id, model, StringComparison.Ordinal)
                            )
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

    private static CatalogLayer ParseEmbedded()
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

    internal static string DefaultManagedFilePath() =>
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

    private static CatalogLayer? ParseOptional(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        return Parse(File.ReadAllText(path), path);
    }

    private static CatalogLayer Parse(string json, string source)
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

        return new CatalogLayer(schemaVersion, models);
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

    /// <summary>
    /// Layers <paramref name="upper"/> over <paramref name="lower"/>. A schema-2 upper entry replaces
    /// the lower entry completely (pricing and capabilities included); a schema-1 upper entry cannot
    /// express <see cref="ModelInfo.Pricing"/>, <see cref="ModelInfo.MaxOutput"/>,
    /// <see cref="ModelInfo.SupportsVision"/> or <see cref="ModelInfo.WireId"/>, so overriding an
    /// existing id keeps those values from the lower entry while taking the upper entry's routing
    /// fields.
    /// </summary>
    private static List<ModelInfo> Merge(List<ModelInfo> lower, CatalogLayer? upper)
    {
        if (upper is null)
        {
            return [.. lower];
        }

        var upperById = upper.Models.ToDictionary(static model => model.Id, StringComparer.Ordinal);
        var lowerIds = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<ModelInfo>(lower.Count + upper.Models.Count);
        foreach (ModelInfo model in lower)
        {
            lowerIds.Add(model.Id);
            if (!upperById.TryGetValue(model.Id, out ModelInfo? overrideModel))
            {
                merged.Add(model);
            }
            else if (upper.SchemaVersion == 1)
            {
                merged.Add(
                    overrideModel with
                    {
                        Pricing = model.Pricing,
                        MaxOutput = model.MaxOutput,
                        SupportsVision = model.SupportsVision,
                        WireId = model.WireId,
                    }
                );
            }
            else
            {
                merged.Add(overrideModel);
            }
        }

        merged.AddRange(upper.Models.Where(model => !lowerIds.Contains(model.Id)));
        return merged;
    }

    private sealed record CatalogLayer(int SchemaVersion, List<ModelInfo> Models);

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
