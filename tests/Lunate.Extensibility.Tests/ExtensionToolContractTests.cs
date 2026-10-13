using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using AgentToolExposure = Lunate.Agent.ToolExposure;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The extension tool contract: the registration carries the core tool model verbatim and the
/// handler receives the core <see cref="ToolContext"/> and returns the core <see cref="ToolResult"/>
/// (design D2).
/// </summary>
public sealed class ExtensionToolContractTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void The_registration_carries_the_core_tool_model()
    {
        JsonElement schema = ObjectSchema();
        JsonElement outputSchema = ObjectSchema();
        var registration = new ExtensionToolRegistration
        {
            Name = "spawn",
            Description = "Spawns a worker.",
            ParametersSchema = schema,
            OutputSchema = outputSchema,
            Annotations = new ToolAnnotations(ReadOnly: true),
            Exposure = AgentToolExposure.Programmatic,
            Risk = ToolRisk.Execute,
            Handler = new DelegateHandler(
                (_, _, _) => Task.FromResult(new ToolResult("ok", false))
            ),
        };

        Assert.Equal("spawn", registration.Name);
        Assert.Equal("Spawns a worker.", registration.Description);
        Assert.Equal(schema, registration.ParametersSchema);
        Assert.Equal(outputSchema, registration.OutputSchema);
        Assert.Equal(new ToolAnnotations(ReadOnly: true), registration.Annotations);
        Assert.Equal(AgentToolExposure.Programmatic, registration.Exposure);
        Assert.Equal(ToolRisk.Execute, registration.Risk);
    }

    [Fact]
    public void The_registration_defaults_to_direct_without_hints_or_overrides()
    {
        var registration = new ExtensionToolRegistration
        {
            Name = "spawn",
            Description = "Spawns a worker.",
            ParametersSchema = ObjectSchema(),
            Handler = new DelegateHandler(
                (_, _, _) => Task.FromResult(new ToolResult("ok", false))
            ),
        };

        Assert.Equal(AgentToolExposure.Direct, registration.Exposure);
        Assert.Null(registration.Annotations);
        Assert.Null(registration.OutputSchema);
        Assert.Null(registration.Risk);
    }

    [Fact]
    public async Task The_handler_receives_the_core_tool_context_and_returns_the_core_result()
    {
        ToolContext toolContext = new("/work", new NullAgentEvents()) { RunId = "run_1" };
        ToolContext? seen = null;
        var result = new ToolResult("spawned", false, StructuredContent: ObjectSchema());
        var registration = new ExtensionToolRegistration
        {
            Name = "spawn",
            Description = "Spawns a worker.",
            ParametersSchema = ObjectSchema(),
            Handler = new DelegateHandler(
                (args, received, _) =>
                {
                    Assert.Equal(JsonValueKind.Object, args.ValueKind);
                    seen = received;
                    return Task.FromResult(result);
                }
            ),
        };

        ToolResult returned = await registration.Handler.InvokeAsync(
            ObjectSchema(),
            toolContext,
            Ct
        );

        Assert.Same(toolContext, seen);
        Assert.Same(result, returned);
    }

    private static JsonElement ObjectSchema() =>
        JsonDocument
            .Parse("""{"type":"object","properties":{"name":{"type":"string"}}}""")
            .RootElement.Clone();

    private sealed class DelegateHandler(
        Func<JsonElement, ToolContext, CancellationToken, Task<ToolResult>> invoke
    ) : IExtensionToolHandler
    {
        public Task<ToolResult> InvokeAsync(
            JsonElement arguments,
            ToolContext context,
            CancellationToken ct
        ) => invoke(arguments, context, ct);
    }
}
