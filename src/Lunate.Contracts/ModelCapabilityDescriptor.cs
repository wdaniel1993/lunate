using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunate.Ai;

/// <summary>
/// Extensible model capability facts. A null category is unknown; an empty set is known to contain
/// no supported values.
/// </summary>
[JsonConverter(typeof(ModelCapabilityDescriptorJsonConverter))]
public sealed class ModelCapabilityDescriptor
{
    public ModelCapabilityDescriptor(
        IReadOnlySet<string>? apis = null,
        IReadOnlySet<string>? inputModalities = null,
        IReadOnlySet<string>? outputModalities = null,
        IReadOnlySet<string>? features = null
    )
    {
        Apis = apis;
        InputModalities = inputModalities;
        OutputModalities = outputModalities;
        Features = features;
    }

    public IReadOnlySet<string>? Apis { get; init; }

    public IReadOnlySet<string>? InputModalities { get; init; }

    public IReadOnlySet<string>? OutputModalities { get; init; }

    public IReadOnlySet<string>? Features { get; init; }

    internal ModelCapabilityDescriptor Overlay(ModelCapabilityDescriptor? higher) =>
        higher is null
            ? this
            : new ModelCapabilityDescriptor(
                higher.Apis ?? Apis,
                higher.InputModalities ?? InputModalities,
                higher.OutputModalities ?? OutputModalities,
                higher.Features ?? Features
            );

    internal static IReadOnlySet<string>? CreateSet(IEnumerable<string>? values) =>
        values is null ? null : new HashSet<string>(values, StringComparer.Ordinal);
}

internal sealed class ModelCapabilityDescriptorJsonConverter
    : JsonConverter<ModelCapabilityDescriptor>
{
    public override ModelCapabilityDescriptor Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Model capabilities must be an object.");
        }

        return new ModelCapabilityDescriptor(
            ReadSet(root, "apis"),
            ReadSet(root, "inputModalities"),
            ReadSet(root, "outputModalities"),
            ReadSet(root, "features")
        );
    }

    public override void Write(
        Utf8JsonWriter writer,
        ModelCapabilityDescriptor value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        WriteSet(writer, "apis", value.Apis);
        WriteSet(writer, "inputModalities", value.InputModalities);
        WriteSet(writer, "outputModalities", value.OutputModalities);
        WriteSet(writer, "features", value.Features);
        writer.WriteEndObject();
    }

    private static IReadOnlySet<string>? ReadSet(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement values))
        {
            return null;
        }

        if (values.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"Capability category '{name}' must be an array.");
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
            {
                throw new JsonException(
                    $"Capability category '{name}' must contain non-empty strings."
                );
            }

            result.Add(value.GetString()!);
        }

        return result;
    }

    private static void WriteSet(Utf8JsonWriter writer, string name, IReadOnlySet<string>? values)
    {
        if (values is null)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (string value in values.Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
