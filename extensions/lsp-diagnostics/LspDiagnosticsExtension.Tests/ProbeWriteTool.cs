using System.Text.Json;
using Lunate.Agent;

namespace LspDiagnosticsExtension.Tests;

/// <summary>
/// A nested-call probe: it forwards its arguments to the real <c>write</c> tool through
/// <see cref="ToolContext.ExecuteToolAsync"/> and keeps the composed <see cref="ToolResult"/> the
/// hook pipeline returned, so a test can inspect the hook-attached structured content that the
/// event stream does not carry.
/// </summary>
internal sealed class ProbeWriteTool : ITool
{
    public string Name => "write-probe";

    public string Description =>
        "Writes through the nested write tool and reports its composed result.";

    public JsonElement ParametersSchema { get; } =
        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

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
        Nested = await execute("write", args, ct);
        return new ToolResult(Nested.Output, Nested.IsError, Nested.Details);
    }
}
