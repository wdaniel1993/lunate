using System.Text.Json;
using Lunate.Agent;

namespace PermissionGateExtension.Tests;

internal sealed class GateTool(string name, ToolAnnotations? annotations) : ITool
{
    public string Name { get; } = name;

    public string Description { get; } = $"Test tool {name}.";

    public JsonElement ParametersSchema { get; } =
        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    public ToolAnnotations? Annotations { get; } = annotations;

    public int Executions { get; private set; }

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        Executions++;
        return Task.FromResult(new ToolResult($"{Name}: ok", IsError: false));
    }
}
