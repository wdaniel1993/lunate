using System.Text.Json;
using Lunate.Agent;

namespace McpFromExtensionExtension.Tests;

/// <summary>
/// A nested-call probe: it forwards the named tool and arguments to
/// <see cref="ToolContext.ExecuteToolAsync"/> and keeps the composed <see cref="ToolResult"/>, so a
/// test can inspect structured content and exposure refusals the event stream does not carry.
/// </summary>
internal sealed class NestedCallProbeTool : ITool
{
    public string Name => "nested-probe";

    public string Description =>
        "Calls another tool through the nested executor and reports its result.";

    public JsonElement ParametersSchema { get; } =
        JsonDocument
            .Parse(
                """{"type":"object","properties":{"tool":{"type":"string"},"args":{"type":"object"}}}"""
            )
            .RootElement.Clone();

    public ToolResult? Nested { get; private set; }

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        var execute =
            ctx.ExecuteToolAsync
            ?? throw new InvalidOperationException("The context has no nested executor.");
        string target = args.GetProperty("tool").GetString()!;
        JsonElement nestedArgs = args.TryGetProperty("args", out JsonElement value)
            ? value.Clone()
            : JsonDocument.Parse("{}").RootElement.Clone();
        Nested = await execute(target, nestedArgs, ct);
        return new ToolResult(Nested.Output, Nested.IsError);
    }
}
