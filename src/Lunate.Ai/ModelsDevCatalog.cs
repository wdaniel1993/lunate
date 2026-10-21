using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lunate.Ai;

/// <summary>
/// A generated catalog: the mapped models, the deterministic schema-2 JSON, the source hash and the
/// warnings for presets the snapshot did not cover.
/// </summary>
public sealed record CatalogSnapshot(
    IReadOnlyList<ModelInfo> Models,
    string Json,
    string SourceSha256
)
{
    /// <summary>
    /// Non-fatal findings, one per preset skipped because the snapshot has no provider for it.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// The one models.dev to catalog mapper; the repo generator and explicit refresh both call it.
/// </summary>
public static class ModelsDevCatalog
{
    public const string SourceName = "models.dev";
    public const string SourceUrl = "https://models.dev/api.json";

    public static CatalogSnapshot Generate(string json, IReadOnlyList<ProviderPreset> presets)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        ArgumentNullException.ThrowIfNull(presets);
        if (presets.Count == 0)
        {
            throw new ArgumentException(
                "At least one provider preset is required.",
                nameof(presets)
            );
        }

        string sourceSha256 = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(json))
        );
        using JsonDocument document = Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"The {SourceName} snapshot is not a JSON object.");
        }

        var models = new List<ModelInfo>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var skippedPresetIds = new List<string>();
        var warnings = new List<string>();
        foreach (ProviderPreset preset in presets)
        {
            if (
                !root.TryGetProperty(preset.Id, out JsonElement provider)
                || provider.ValueKind != JsonValueKind.Object
            )
            {
                skippedPresetIds.Add(preset.Id);
                warnings.Add(
                    $"provider '{preset.Id}' is not in the {SourceName} snapshot; skipped"
                );
                continue;
            }

            JsonElement providerModels = RequireObject(
                provider,
                "models",
                $"Provider '{preset.Id}' in the {SourceName} snapshot has no models object."
            );
            foreach (string modelKey in SortedKeys(providerModels))
            {
                JsonElement entry = providerModels.GetProperty(modelKey);
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        $"Model '{modelKey}' for provider '{preset.Id}' in the {SourceName} snapshot is not an object."
                    );
                }

                ModelInfo model = Map(preset, modelKey, entry);
                if (!ids.Add(model.Id))
                {
                    throw new InvalidDataException(
                        $"Duplicate catalog model id '{model.Id}' generated from {SourceName}."
                    );
                }

                models.Add(model);
            }
        }

        if (models.Count == 0)
        {
            throw new InvalidDataException(
                skippedPresetIds.Count == 0
                    ? $"The {SourceName} snapshot generated no models for any provider preset; refusing to write an empty catalog."
                    : $"The {SourceName} snapshot has no provider for {NameList(skippedPresetIds)}; refusing to write an empty catalog."
            );
        }

        ValidateUniqueWireIds(models);
        return new CatalogSnapshot(models, Serialize(models, sourceSha256), sourceSha256)
        {
            Warnings = warnings,
        };
    }

    private static string NameList(IReadOnlyList<string> ids) =>
        string.Join(", ", ids.Select(static id => $"'{id}'"));

    internal static void ValidateUniqueWireIds(IEnumerable<ModelInfo> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        var seen = new HashSet<(string Provider, string WireId)>();
        foreach (ModelInfo model in models)
        {
            if (model.WireId is not string wireId)
            {
                continue;
            }

            if (!seen.Add((model.Provider, wireId)))
            {
                throw new InvalidDataException(
                    $"Duplicate wire id '{wireId}' for provider '{model.Provider}' in the generated catalog."
                );
            }
        }
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The {SourceName} snapshot is not valid JSON: {exception.Message}",
                exception
            );
        }
    }

    private static JsonElement RequireObject(JsonElement parent, string name, string failure)
    {
        if (
            parent.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Object
        )
        {
            return value;
        }

        throw new InvalidDataException(failure);
    }

    private static List<string> SortedKeys(JsonElement models)
    {
        var keys = new List<string>();
        foreach (JsonProperty property in models.EnumerateObject())
        {
            keys.Add(property.Name);
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    private static ModelInfo Map(ProviderPreset preset, string modelKey, JsonElement entry)
    {
        bool native = preset.Adapter is ProviderAdapter.OpenAi or ProviderAdapter.Anthropic;
        string? reasoning = ReadBoolean(entry, "reasoning")
            ? preset.Adapter == ProviderAdapter.Anthropic
                ? "budget"
                : "effort"
            : null;

        return new ModelInfo(
            native ? modelKey : $"{preset.Id}/{modelKey}",
            preset.Id,
            Endpoint: null,
            ReadContext(entry),
            ReadBoolean(entry, "tool_call")
        )
        {
            Reasoning = reasoning,
            Pricing = ReadPricing(entry),
            MaxOutput = ReadMaxOutput(entry),
            SupportsVision = ReadBoolean(entry, "attachment"),
            WireId = native ? null : modelKey,
        };
    }

    private static int ReadContext(JsonElement entry) => ReadLimit(entry, "context") ?? 0;

    private static int? ReadMaxOutput(JsonElement entry) =>
        ReadLimit(entry, "output") is int value && value > 0 ? value : null;

    private static int? ReadLimit(JsonElement entry, string name)
    {
        if (
            !entry.TryGetProperty("limit", out JsonElement limit)
            || limit.ValueKind != JsonValueKind.Object
        )
        {
            return null;
        }

        return
            limit.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    private static bool ReadBoolean(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static ModelPricing? ReadPricing(JsonElement entry) =>
        entry.TryGetProperty("cost", out JsonElement cost) && cost.ValueKind == JsonValueKind.Object
            ? new ModelPricing(
                ReadDecimal(cost, "input"),
                ReadDecimal(cost, "output"),
                ReadDecimal(cost, "cache_read"),
                ReadDecimal(cost, "cache_write")
            )
            : null;

    private static decimal? ReadDecimal(JsonElement cost, string name) =>
        cost.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDecimal(out decimal rate)
            ? rate
            : null;

    private static string Serialize(IReadOnlyList<ModelInfo> models, string sourceSha256)
    {
        var builder = new StringBuilder();
        builder.Append("{\n");
        builder.Append("  \"schemaVersion\": 2,\n");
        builder.Append(
            $"  \"source\": {{ \"name\": \"{SourceName}\", \"url\": \"{SourceUrl}\", \"sha256\": \"{sourceSha256}\" }},\n"
        );
        builder.Append("  \"models\": [\n");
        for (int index = 0; index < models.Count; index++)
        {
            builder.Append("    ").Append(SerializeModel(models[index]));
            builder.Append(index < models.Count - 1 ? ",\n" : "\n");
        }

        builder.Append("  ]\n}\n");
        return builder.ToString();
    }

    private static string SerializeModel(ModelInfo model)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", model.Id);
            writer.WriteString("provider", model.Provider);
            if (model.WireId is string wireId)
            {
                writer.WriteString("wireId", wireId);
            }

            writer.WriteNumber("contextWindow", model.ContextWindow);
            writer.WriteBoolean("supportsTools", model.SupportsTools);
            if (model.MaxOutput is int maxOutput)
            {
                writer.WriteNumber("maxOutput", maxOutput);
            }

            if (model.SupportsVision)
            {
                writer.WriteBoolean("supportsVision", true);
            }

            if (model.Pricing is ModelPricing pricing)
            {
                WritePricing(writer, pricing);
            }

            if (model.Reasoning is string reasoning)
            {
                writer.WriteString("reasoning", reasoning);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePricing(Utf8JsonWriter writer, ModelPricing pricing)
    {
        writer.WriteStartObject("pricing");
        if (pricing.Input is decimal input)
        {
            writer.WriteNumber("input", input);
        }

        if (pricing.Output is decimal output)
        {
            writer.WriteNumber("output", output);
        }

        if (pricing.CacheRead is decimal cacheRead)
        {
            writer.WriteNumber("cacheRead", cacheRead);
        }

        if (pricing.CacheWrite is decimal cacheWrite)
        {
            writer.WriteNumber("cacheWrite", cacheWrite);
        }

        writer.WriteEndObject();
    }
}
