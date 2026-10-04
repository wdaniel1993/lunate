using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Ai;

public sealed class ModelCatalog
{
    private const string EmbeddedResourceName = "Lunate.Ai.models.json";
    private const int SupportedSchemaVersion = 1;

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
        List<ModelInfo> embedded = ParseEmbedded();
        string path = userFilePath ?? DefaultUserFilePath();
        if (!File.Exists(path))
        {
            return new ModelCatalog(embedded);
        }

        List<ModelInfo> user = Parse(File.ReadAllText(path), path);
        return new ModelCatalog(Merge(embedded, user));
    }

    public ModelInfo? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _modelsById.GetValueOrDefault(id);
    }

    private static List<ModelInfo> ParseEmbedded()
    {
        using Stream? stream = typeof(ModelCatalog).Assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"The embedded resource '{EmbeddedResourceName}' was not found.");
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), "built-in models.json");
    }

    private static string DefaultUserFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lunate",
            "models.json");

    private static List<ModelInfo> Parse(string json, string source)
    {
        CatalogFile? file = JsonSerializer.Deserialize<CatalogFile>(json, AIJsonUtilities.DefaultOptions);
        if (file?.SchemaVersion != SupportedSchemaVersion)
        {
            string version = file?.SchemaVersion is int value
                ? value.ToString(CultureInfo.InvariantCulture)
                : "missing";
            throw new InvalidDataException(
                $"Unsupported schemaVersion '{version}' in {source}; expected {SupportedSchemaVersion}.");
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
            throw new InvalidDataException($"Model '{entry.Id}' in {source} is missing a provider.");
        }

        return new ModelInfo(entry.Id, entry.Provider, entry.Endpoint, entry.ContextWindow, entry.SupportsTools);
    }

    private static List<ModelInfo> Merge(List<ModelInfo> embedded, List<ModelInfo> user)
    {
        var userById = user.ToDictionary(static model => model.Id, StringComparer.Ordinal);
        var embeddedIds = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<ModelInfo>(embedded.Count + user.Count);
        foreach (ModelInfo model in embedded)
        {
            embeddedIds.Add(model.Id);
            merged.Add(userById.TryGetValue(model.Id, out ModelInfo? userModel) ? userModel : model);
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
    }
}
