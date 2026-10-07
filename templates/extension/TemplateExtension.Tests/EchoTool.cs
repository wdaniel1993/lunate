using System.Text.Json;
using Lunate.Agent;

namespace TemplateExtension.Tests;

internal sealed class EchoTool : ITool
{
    public string Name => "echo";

    public string Description => "Echoes the given text back.";

    public JsonElement ParametersSchema { get; } =
        JsonDocument
            .Parse(
                """{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}"""
            )
            .RootElement.Clone();

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        string text = args.TryGetProperty("text", out JsonElement value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
        return Task.FromResult(new ToolResult($"echo: {text}", IsError: false));
    }
}
