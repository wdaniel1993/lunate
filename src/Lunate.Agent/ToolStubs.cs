using System.Text.Json;

namespace Lunate.Agent;

/// <summary>
/// Typed stub generation from a tool's JSON schemas. The mapping never fails a run: constructs it
/// does not recognise degrade to <c>any</c> with a <see cref="ToolStubParameter.Note"/> naming the
/// construct, and an absent or non-object parameter schema yields zero parameters.
/// </summary>
public static class ToolStubs
{
    /// <summary>Schema constructs the mapping does not model; their presence degrades the parameter.</summary>
    private static readonly string[] UnsupportedConstructs = ["oneOf", "anyOf", "allOf", "$ref"];

    /// <summary>Derives the typed stub of one tool from its parameter and output schemas.</summary>
    public static ToolStub Describe(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return new ToolStub(
            tool.Name,
            tool.Description,
            DescribeParameters(tool.ParametersSchema),
            tool.OutputSchema is not null
        );
    }

    private static IReadOnlyList<ToolStubParameter> DescribeParameters(JsonElement schema)
    {
        if (
            schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("properties", out JsonElement properties)
            || properties.ValueKind != JsonValueKind.Object
        )
        {
            return [];
        }

        HashSet<string> required = ReadRequired(schema);
        List<ToolStubParameter> parameters = [];
        foreach (JsonProperty property in properties.EnumerateObject())
        {
            parameters.Add(DescribeParameter(property.Name, property.Value, required));
        }

        return parameters;
    }

    private static HashSet<string> ReadRequired(JsonElement schema)
    {
        HashSet<string> required = new(StringComparer.Ordinal);
        if (
            schema.TryGetProperty("required", out JsonElement list)
            && list.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement element in list.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String && element.GetString() is { } name)
                {
                    required.Add(name);
                }
            }
        }

        return required;
    }

    private static ToolStubParameter DescribeParameter(
        string name,
        JsonElement property,
        HashSet<string> required
    )
    {
        if (property.ValueKind != JsonValueKind.Object)
        {
            return new ToolStubParameter(
                name,
                "any",
                required.Contains(name),
                null,
                null,
                null,
                "unsupported schema construct: the property schema is not an object"
            );
        }

        IReadOnlyList<string>? enumValues = ReadEnum(property);
        (string type, string? note) = MapType(property, enumValues);
        return new ToolStubParameter(
            name,
            type,
            required.Contains(name),
            ReadString(property, "description"),
            ReadDefault(property),
            enumValues,
            note
        );
    }

    private static (string Type, string? Note) MapType(
        JsonElement property,
        IReadOnlyList<string>? enumValues
    )
    {
        if (enumValues is { Count: > 0 })
        {
            return ("enum", null);
        }

        if (
            !property.TryGetProperty("type", out JsonElement typeElement)
            || typeElement.ValueKind != JsonValueKind.String
        )
        {
            foreach (string construct in UnsupportedConstructs)
            {
                if (property.TryGetProperty(construct, out _))
                {
                    return ("any", $"unsupported schema construct: {construct}");
                }
            }

            return ("any", "missing type");
        }

        string type = typeElement.GetString()!;
        return type switch
        {
            "string" or "number" or "integer" or "boolean" or "array" or "object" => (type, null),
            _ => ("any", $"unsupported schema type: {type}"),
        };
    }

    private static string? ReadString(JsonElement property, string name) =>
        property.TryGetProperty(name, out JsonElement element)
        && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static string? ReadDefault(JsonElement property) =>
        property.TryGetProperty("default", out JsonElement element)
        && element.ValueKind != JsonValueKind.Null
            ? element.GetRawText()
            : null;

    private static IReadOnlyList<string>? ReadEnum(JsonElement property)
    {
        if (
            !property.TryGetProperty("enum", out JsonElement enumElement)
            || enumElement.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }

        List<string> values = [];
        foreach (JsonElement element in enumElement.EnumerateArray())
        {
            values.Add(
                element.ValueKind == JsonValueKind.String
                    ? element.GetString()!
                    : element.GetRawText()
            );
        }

        return values;
    }
}
