using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;
using Lunate.Extensibility.Testing;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// The published JSON Schema: every payload DTO and result family has a definition, and every
/// union variant's <c>outcome</c> constant matches what the codec actually emits — the schema can
/// never drift from the wire.
/// </summary>
public sealed class RemoteJsonSchemaTests
{
    [Fact]
    public void Every_payload_and_result_family_has_a_definition()
    {
        JsonNode schema = JsonNode.Parse(RemoteJsonSchema.Build())!;
        JsonNode definitions = schema["$defs"]!;

        foreach (
            Type payloadType in RemoteConformance
                .Payloads.Select(s => s.Contract.PayloadType)
                .Distinct()
        )
        {
            Assert.True(
                definitions[payloadType.Name] is not null,
                $"the schema has no definition for payload {payloadType.Name}"
            );
        }

        foreach (HookContract contract in HookContracts.All.Where(c => c.ResultType is not null))
        {
            Assert.True(
                definitions[contract.ResultType!.Name] is not null,
                $"the schema has no definition for {contract.ResultType.Name}"
            );
        }

        Assert.NotNull(definitions["HookCallEnvelope"]);
        Assert.NotNull(definitions["ServiceCallEnvelope"]);
        Assert.NotNull(definitions["InitializeRequest"]);
        Assert.NotNull(definitions["InitializeResult"]);
    }

    [Fact]
    public void Union_outcome_constants_match_the_codec()
    {
        JsonNode schema = JsonNode.Parse(RemoteJsonSchema.Build())!;

        foreach (HookResultSample sample in RemoteConformance.Results)
        {
            Type variant = sample.Result.GetType();
            if (variant.DeclaringType is null)
            {
                continue;
            }

            JsonElement encoded = HookResultCodec.EncodeResult(sample.Contract, sample.Result);
            JsonObject family = schema["$defs"]![variant.DeclaringType.Name]!.AsObject();
            JsonArray variants = family["oneOf"]!.AsArray();
            JsonNode? entry = variants.FirstOrDefault(item =>
                item!["title"]?.GetValue<string>() == variant.Name
            );

            Assert.True(
                entry is not null,
                $"{variant.Name}: no oneOf entry in {variant.DeclaringType.Name}"
            );
            string expected = entry!["properties"]!["outcome"]!["const"]!.GetValue<string>();
            string actual = encoded.GetProperty("outcome").GetString()!;
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void The_schema_is_deterministic()
    {
        Assert.Equal(RemoteJsonSchema.Build(), RemoteJsonSchema.Build());
    }
}
