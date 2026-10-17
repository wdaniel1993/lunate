using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility.Testing;

/// <summary>
/// Generates the JSON Schema of the remote wire from the hook and service DTOs, so non-C# authors
/// never reverse-engineer C#. The document describes the <c>initialize</c> request/result, the
/// hook call envelope with every payload, every hook's result family (union variants carry an
/// <c>outcome</c> constant) and the service-call envelope. It is shipped with
/// <c>Lunate.Extensibility.Testing</c> and published with the documentation; a drift test pins the
/// published copy to <see cref="Build"/>.
/// </summary>
public static class RemoteJsonSchema
{
    public static string Build()
    {
        var definitions = new JsonObject();
        foreach (
            Type payloadType in RemoteConformance
                .Payloads.Select(sample => sample.Contract.PayloadType)
                .Distinct()
        )
        {
            definitions[payloadType.Name] = SchemaOf(payloadType);
        }

        foreach (HookContract contract in HookContracts.All.Where(c => c.ResultType is not null))
        {
            definitions[contract.ResultType!.Name] = ResultFamilySchema(contract);
        }

        definitions["HookCallEnvelope"] = SchemaOf(typeof(HookCallEnvelope));
        definitions["RemoteCallContext"] = SchemaOf(typeof(RemoteCallContext));
        definitions["ServiceCallEnvelope"] = SchemaOf(typeof(ServiceCallEnvelope));
        definitions["WorkspaceInfo"] = SchemaOf(typeof(WorkspaceInfo));
        definitions["InitializeRequest"] = SchemaOf(typeof(InitializeRequest));
        definitions["InitializeResult"] = SchemaOf(typeof(InitializeResult));

        var hookResults = new JsonObject();
        foreach (HookContract contract in HookContracts.All.Where(c => c.ResultType is not null))
        {
            hookResults[contract.Name] = Ref(contract.ResultType!.Name);
        }

        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://lunate.dev/schemas/remote-extension-wire.json",
            ["title"] = $"Lunate remote extension wire (protocol version {RemoteProtocol.Version})",
            ["description"] =
                "JSON-RPC 2.0 over newline-delimited UTF-8 JSON on stdio. Stdout is protocol-only; a non-JSON line or a line over 16 MB is a protocol error. Interceptor hooks are requests, observer hooks are notifications, service-call is a request, and $/cancelRequest carries { id }.",
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["initialize.request"] = Ref("InitializeRequest"),
                ["initialize.result"] = Ref("InitializeResult"),
                ["hook.request"] = Ref("HookCallEnvelope"),
                ["hook.results"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = hookResults,
                },
                ["service-call"] = Ref("ServiceCallEnvelope"),
                ["cancel"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { ["id"] = AnyId() },
                    ["required"] = new JsonArray { "id" },
                },
            },
            ["$defs"] = definitions,
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The schema definition name a variant/type is published under.</summary>
    public static string DefinitionName(Type type) => type.Name;

    private static JsonObject ResultFamilySchema(HookContract contract)
    {
        var oneOf = new JsonArray();
        foreach (Type variant in contract.ResultTypes.Where(type => type != contract.ResultType))
        {
            JsonObject schema = SchemaOf(variant);
            JsonObject properties = schema["properties"] as JsonObject ?? new JsonObject();
            properties["outcome"] = new JsonObject
            {
                ["const"] = OutcomeName(variant),
                ["description"] = "the result variant discriminator",
            };
            schema["properties"] = properties;
            EnsureRequired(schema, "outcome");
            schema["title"] = variant.Name;
            oneOf.Add(schema);
        }

        // Concrete families (ContextBuildingResult, ToolResultReadyResult) have no variants:
        // their schema is the DTO itself.
        if (oneOf.Count == 0)
        {
            return SchemaOf(contract.ResultType!);
        }

        return new JsonObject { ["oneOf"] = oneOf };
    }

    private static void EnsureRequired(JsonObject schema, string name)
    {
        JsonArray required = schema["required"] as JsonArray ?? new JsonArray();
        if (!required.Any(item => item?.GetValue<string>() == name))
        {
            required.Add(name);
        }

        schema["required"] = required;
    }

    private static JsonObject AnyId() =>
        new()
        {
            ["description"] = "the request id being cancelled (string or number)",
            ["type"] = new JsonArray { "string", "number" },
        };

    /// <summary>The <c>outcome</c> constant for one union variant; the conformance suite pins it.</summary>
    public static string OutcomeName(Type variant) =>
        variant.Name switch
        {
            "Allow" => "allow",
            "Deny" => "deny",
            "PassThrough" => "passThrough",
            "Transform" => "transform",
            "Consume" => "consume",
            "None" => "none",
            "Apply" => "apply",
            "Keep" => "keep",
            "Replace" => "replace",
            "Proceed" => "proceed",
            "Block" => "block",
            "TurnEnded" => "turnEnded",
            "UseDefault" => "useDefault",
            "Provide" => "provide",
            _ => throw new InvalidOperationException(
                $"no outcome name is defined for result variant '{variant.FullName}'."
            ),
        };

    private static JsonObject Ref(string definition) =>
        new() { ["$ref"] = $"#/$defs/{definition}" };

    private static JsonObject SchemaOf(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string))
        {
            return new JsonObject { ["type"] = "string" };
        }

        if (type == typeof(bool))
        {
            return new JsonObject { ["type"] = "boolean" };
        }

        if (type == typeof(int) || type == typeof(long))
        {
            return new JsonObject { ["type"] = "integer" };
        }

        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            return new JsonObject { ["type"] = "number" };
        }

        if (type == typeof(JsonElement) || type == typeof(JsonElement?))
        {
            return new JsonObject { ["description"] = "any JSON value" };
        }

        if (type.IsArray)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = SchemaOf(type.GetElementType()!),
            };
        }

        if (TryGetEnumerableItem(type) is { } itemType)
        {
            return new JsonObject { ["type"] = "array", ["items"] = SchemaOf(itemType) };
        }

        if (TryGetDictionaryValue(type) is { } valueType)
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = SchemaOf(valueType),
            };
        }

        var properties = new JsonObject();
        var required = new JsonArray();
        var nullability = new NullabilityInfoContext();
        foreach (
            PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        )
        {
            if (property.GetMethod is null || property.Name == "EqualityContract")
            {
                continue;
            }

            string name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            JsonObject propertySchema = SchemaOf(property.PropertyType);
            NullabilityInfo info = nullability.Create(property);
            if (info.ReadState is NullabilityState.Nullable)
            {
                if (NullableType(propertySchema) is { } nullable)
                {
                    propertySchema["type"] = nullable;
                }
            }
            else
            {
                required.Add(name);
            }

            properties[name] = propertySchema;
        }

        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    private static JsonNode? NullableType(JsonObject schema)
    {
        if (schema["type"] is JsonArray array)
        {
            array.Add("null");
            return array;
        }

        if (schema["type"] is JsonValue value)
        {
            return new JsonArray { value.GetValue<string>(), "null" };
        }

        // A schema without a "type" (an "any JSON value" placeholder) is already nullable.
        return null;
    }

    private static Type? TryGetEnumerableItem(Type type)
    {
        if (!type.IsGenericType)
        {
            return null;
        }

        Type definition = type.GetGenericTypeDefinition();
        if (definition == typeof(IReadOnlyList<>) || definition == typeof(List<>))
        {
            return type.GetGenericArguments()[0];
        }

        return null;
    }

    private static Type? TryGetDictionaryValue(Type type)
    {
        if (
            type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
            && type.GetGenericArguments()[0] == typeof(string)
        )
        {
            return type.GetGenericArguments()[1];
        }

        return null;
    }
}
