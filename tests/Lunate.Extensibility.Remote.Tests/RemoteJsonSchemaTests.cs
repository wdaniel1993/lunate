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
        // The command-invocation wire (protocol version 2) is published with the same generator.
        Assert.NotNull(definitions["CommandInvokeRequest"]);
        Assert.NotNull(definitions["CommandInvokeResult"]);
        Assert.Equal(
            ["completed", "cancelled", "failed"],
            definitions["CommandInvokeDisposition"]!["enum"]!
                .AsArray()
                .Select(value => value!.GetValue<string>())
        );
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

    [Fact]
    public void The_published_schema_matches_the_generator()
    {
        string path = Path.Combine(FindRepositoryRoot(), "docs", "extensions-remote.schema.json");

        Assert.True(File.Exists(path), $"the published schema is missing: {path}");
        Assert.Equal(
            RemoteJsonSchema.Build().ReplaceLineEndings("\n"),
            File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd('\n')
        );
    }

    private static string FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find lunate.sln above {AppContext.BaseDirectory}."
        );
    }
}
