using System.Text.Json;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>
/// The typed stub mapping (schema constructs, degrade-with-note, no-schema cases) and the per-run
/// stamped callable set (exposure filtering, registry order, null outside a run, narrowed children).
/// </summary>
public sealed class ToolStubsTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void The_mapping_table_is_covered_in_declaration_order()
    {
        var tool = new ScriptedTool(
            "mapped",
            "A mapped tool.",
            """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "the path" },
                "path2": { "type": "string", "description": "another" },
                "limit": { "type": "integer" },
                "ratio": { "type": "number", "default": 0.5 },
                "recursive": { "type": "boolean" },
                "paths": { "type": "array" },
                "options": { "type": "object" },
                "mode": { "type": "string", "enum": ["fast", "slow"] },
                "filter": { "oneOf": [{ "type": "string" }, { "type": "number" }] }
              },
              "required": ["path", "mode"]
            }
            """
        );

        ToolStub stub = ToolStubs.Describe(tool);

        Assert.Equal("mapped", stub.Name);
        Assert.Equal("A mapped tool.", stub.Description);
        Assert.False(stub.HasStructuredOutput);
        Assert.Equal(
            ["path", "path2", "limit", "ratio", "recursive", "paths", "options", "mode", "filter"],
            stub.Parameters.Select(parameter => parameter.Name)
        );
        Assert.Equal(
            ["string", "string", "integer", "number", "boolean", "array", "object", "enum", "any"],
            stub.Parameters.Select(parameter => parameter.Type)
        );
        Assert.True(stub.Parameters[0].Required);
        Assert.False(stub.Parameters[1].Required);
        Assert.True(stub.Parameters[7].Required);
        Assert.Equal("the path", stub.Parameters[0].Description);
        Assert.Equal("0.5", stub.Parameters[3].Default);
        Assert.Null(stub.Parameters[0].Default);
        Assert.Equal(["fast", "slow"], stub.Parameters[7].Enum);
        Assert.Null(stub.Parameters[0].Note);
        Assert.Equal("unsupported schema construct: oneOf", stub.Parameters[8].Note);
    }

    [Fact]
    public void A_declared_output_schema_is_reported()
    {
        var tool = new ScriptedTool("structured", "Structured.", """{"type":"object"}""")
        {
            OutputSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
        };

        Assert.True(ToolStubs.Describe(tool).HasStructuredOutput);
    }

    [Fact]
    public void An_absent_or_non_object_schema_yields_zero_parameters()
    {
        Assert.Empty(
            ToolStubs
                .Describe(new ScriptedTool("a", "No properties.", """{"type":"object"}"""))
                .Parameters
        );
        Assert.Empty(
            ToolStubs
                .Describe(new ScriptedTool("b", "Not an object.", """{"type":"string"}"""))
                .Parameters
        );
        Assert.Empty(ToolStubs.Describe(new ScriptedTool("c", "Not JSON.", "[]")).Parameters);
    }

    [Fact]
    public void Unrecognised_constructs_degrade_to_any_with_a_note()
    {
        var tool = new ScriptedTool(
            "degraded",
            "Degraded.",
            """
            {
              "type": "object",
              "properties": {
                "missing": { "description": "no type" },
                "unknown": { "type": "date" },
                "ref": { "$ref": "#/definitions/x" },
                "anyOf": { "anyOf": [{ "type": "string" }] },
                "booleanSchema": true
              }
            }
            """
        );

        ToolStub stub = ToolStubs.Describe(tool);

        Assert.All(stub.Parameters, parameter => Assert.Equal("any", parameter.Type));
        Assert.Equal("missing type", stub.Parameters[0].Note);
        Assert.Equal("unsupported schema type: date", stub.Parameters[1].Note);
        Assert.Equal("unsupported schema construct: $ref", stub.Parameters[2].Note);
        Assert.Equal("unsupported schema construct: anyOf", stub.Parameters[3].Note);
        Assert.Equal(
            "unsupported schema construct: the property schema is not an object",
            stub.Parameters[4].Note
        );
    }

    [Fact]
    public async Task The_stamped_list_is_the_callable_set_in_registry_order()
    {
        IReadOnlyList<ToolStub>? stamped = null;
        var probe = new ScriptedTool("deferred-probe", "Reads stubs.", """{"type":"object"}""")
        {
            Exposure = ToolExposure.Deferred,
            OnExecuteAsync = (_, ctx, _) =>
            {
                stamped = ctx.ToolStubs;
                return Task.FromResult(new ToolResult("probed", IsError: false));
            },
        };
        var direct = ReadTool("direct");
        var programmatic = new ScriptedTool("prog", "Programmatic.", """{"type":"object"}""")
        {
            Exposure = ToolExposure.Programmatic,
        };
        var modelOnly = new ScriptedTool("model-only", "Model only.", """{"type":"object"}""")
        {
            Exposure = ToolExposure.ModelOnly,
        };
        var hidden = new ScriptedTool("hidden", "Hidden.", """{"type":"object"}""")
        {
            Exposure = ToolExposure.Hidden,
        };
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "deferred-probe", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(direct, modelOnly, probe, hidden, programmatic)
        );

        await Run(harness);

        Assert.NotNull(stamped);
        Assert.Equal(["read", "deferred-probe", "prog"], stamped.Select(stub => stub.Name));
    }

    [Fact]
    public async Task The_stamped_list_is_null_outside_a_run()
    {
        IReadOnlyList<ToolStub>? stamped = [new ToolStub("placeholder", "placeholder", [], false)];
        var probe = new ScriptedTool("probe", "Reads stubs.", """{"type":"object"}""")
        {
            OnExecuteAsync = (_, ctx, _) =>
            {
                stamped = ctx.ToolStubs;
                return Task.FromResult(new ToolResult("probed", IsError: false));
            },
        };

        await probe.ExecuteAsync(
            JsonDocument.Parse("{}").RootElement.Clone(),
            new ToolContext("unused", new RecordingAgentEvents()),
            Ct
        );

        Assert.Null(stamped);
    }

    [Fact]
    public async Task A_narrowed_child_registry_narrows_its_stubs()
    {
        IReadOnlyList<ToolStub>? childStubs = null;
        var probe = new ScriptedTool("child-probe", "Reads stubs.", """{"type":"object"}""")
        {
            OnExecuteAsync = (_, ctx, _) =>
            {
                childStubs = ctx.ToolStubs;
                return Task.FromResult(new ToolResult("probed", IsError: false));
            },
        };
        var extra = new ScriptedTool("extra", "Not allowed in the child.", """{"type":"object"}""");
        var spawn = new ScriptedTool(
            "spawn",
            "Spawns a narrowed subagent.",
            """{"type":"object","properties":{"prompt":{"type":"string"}}}""",
            ToolRisk.Write
        );
        spawn.OnExecuteAsync = async (_, ctx, ct) =>
        {
            SubagentResult result = await ctx.Subagents!.RunAsync(
                new SubagentRequest("child prompt", AllowedTools: ["child-probe"]),
                ct
            );
            return new ToolResult(result.Output, result.IsError);
        };
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_1", "child-probe", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("child done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(probe, extra, spawn));

        await Run(harness);

        Assert.NotNull(childStubs);
        Assert.Equal(["child-probe"], childStubs.Select(stub => stub.Name));
    }
}
