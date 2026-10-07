using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Extensibility.Testing.Tests;

internal sealed class TestTool(string name, string output) : ITool
{
    public string Name { get; } = name;

    public string Description { get; } = name;

    public JsonElement ParametersSchema { get; } =
        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct) =>
        Task.FromResult(new ToolResult(output, IsError: false));
}
