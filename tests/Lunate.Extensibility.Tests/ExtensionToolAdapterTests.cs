using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using AgentToolExposure = Lunate.Agent.ToolExposure;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The adapter and its per-extension lifetime: registrations map onto the core tool model, explicit
/// risk wins over annotations, and unloading an extension cancels its in-flight calls into a
/// cancelled result (design D5, D7, D8).
/// </summary>
public sealed class ExtensionToolAdapterTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void The_adapter_maps_the_registration_to_the_core_tool_model()
    {
        var registry = new ExtensionToolRegistry();
        registry.Register(
            "spawner",
            new ExtensionToolRegistration
            {
                Name = "spawn",
                Description = "Spawns a worker.",
                ParametersSchema = Schema(),
                OutputSchema = Schema(),
                Annotations = new ToolAnnotations(Destructive: true),
                Exposure = AgentToolExposure.Programmatic,
                Handler = EchoHandler("ok"),
            }
        );

        ExtensionToolAdapter tool = Assert.Single(registry.List());

        Assert.Equal("spawner__spawn", tool.Name);
        Assert.Equal("spawner", tool.ExtensionId);
        Assert.Equal("Spawns a worker.", tool.Description);
        Assert.Equal(Schema().GetRawText(), tool.ParametersSchema.GetRawText());
        Assert.Equal(Schema().GetRawText(), tool.OutputSchema?.GetRawText());
        Assert.Equal(new ToolAnnotations(Destructive: true), tool.Annotations);
        Assert.Equal(AgentToolExposure.Programmatic, tool.Exposure);
        Assert.Equal("spawner", tool.Namespace?.Name);
        Assert.Equal(ToolRisk.Write, tool.Risk);
    }

    [Fact]
    public void No_annotations_derive_a_write_risk_and_an_explicit_risk_wins()
    {
        var registry = new ExtensionToolRegistry();
        registry.Register(
            "spawner",
            new ExtensionToolRegistration
            {
                Name = "plain",
                Description = "No hints.",
                ParametersSchema = Schema(),
                Handler = EchoHandler("ok"),
            }
        );
        registry.Register(
            "spawner",
            new ExtensionToolRegistration
            {
                Name = "pinned",
                Description = "Pinned read-only despite destructive hints.",
                ParametersSchema = Schema(),
                Annotations = new ToolAnnotations(Destructive: true),
                Risk = ToolRisk.ReadOnly,
                Handler = EchoHandler("ok"),
            }
        );

        IReadOnlyList<ExtensionToolAdapter> tools = registry.List();

        Assert.Equal(ToolRisk.Write, tools[0].Risk);
        Assert.Equal(ToolRisk.ReadOnly, tools[1].Risk);
    }

    [Fact]
    public async Task An_in_flight_call_is_cancelled_when_the_extension_unloads()
    {
        var registry = new ExtensionToolRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.Register(
            "slow",
            new ExtensionToolRegistration
            {
                Name = "wait",
                Description = "Waits.",
                ParametersSchema = Schema(),
                Handler = new DelegateToolHandler(
                    async (_, _, ct) =>
                    {
                        entered.TrySetResult();
                        await Task.Delay(Timeout.Infinite, ct);
                        return new ToolResult("never", false);
                    }
                ),
            }
        );
        ExtensionToolAdapter tool = Assert.Single(registry.List());
        var context = new ToolContext("/work", new NullAgentEvents());
        Task<ToolResult> call = tool.ExecuteAsync(Schema(), context, Ct);
        await entered.Task;

        IReadOnlyList<string> removed = registry.Unregister("slow");

        ToolResult result = await call;
        Assert.Equal(["slow__wait"], removed);
        Assert.True(result.IsError);
        Assert.Contains("slow__wait", result.Output, StringComparison.Ordinal);
        Assert.Contains("unloaded", result.Output, StringComparison.Ordinal);
        Assert.Empty(registry.List());
    }

    [Fact]
    public async Task A_cancelled_extension_call_does_not_cancel_the_callers_token()
    {
        var registry = new ExtensionToolRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.Register(
            "slow",
            new ExtensionToolRegistration
            {
                Name = "wait",
                Description = "Waits.",
                ParametersSchema = Schema(),
                Handler = new DelegateToolHandler(
                    async (_, _, ct) =>
                    {
                        entered.TrySetResult();
                        await Task.Delay(Timeout.Infinite, ct);
                        return new ToolResult("never", false);
                    }
                ),
            }
        );
        using var caller = new CancellationTokenSource();
        ExtensionToolAdapter tool = Assert.Single(registry.List());
        Task<ToolResult> call = tool.ExecuteAsync(
            Schema(),
            new ToolContext("/work", new NullAgentEvents()),
            caller.Token
        );
        await entered.Task;

        registry.Unregister("slow");
        ToolResult result = await call;

        Assert.True(result.IsError);
        Assert.False(caller.IsCancellationRequested);
    }

    [Fact]
    public async Task A_handler_result_passes_through_untouched()
    {
        var registry = new ExtensionToolRegistry();
        var expected = new ToolResult("structured", false, StructuredContent: Schema());
        registry.Register(
            "tools",
            new ExtensionToolRegistration
            {
                Name = "echo",
                Description = "Echoes.",
                ParametersSchema = Schema(),
                Handler = new DelegateToolHandler((_, _, _) => Task.FromResult(expected)),
            }
        );

        ToolResult result = await Assert
            .Single(registry.List())
            .ExecuteAsync(Schema(), new ToolContext("/work", new NullAgentEvents()), Ct);

        Assert.Same(expected, result);
    }

    private static JsonElement Schema() =>
        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    private static IExtensionToolHandler EchoHandler(string output) =>
        new DelegateToolHandler((_, _, _) => Task.FromResult(new ToolResult(output, false)));
}

internal sealed class DelegateToolHandler(
    Func<JsonElement, ToolContext, CancellationToken, Task<ToolResult>> invoke
) : IExtensionToolHandler
{
    public Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken ct
    ) => invoke(arguments, context, ct);
}
