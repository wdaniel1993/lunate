using System.Text.Json;

namespace Lunate.Agent.Tests;

internal sealed class ScriptedTool(
    string name,
    string description,
    string schemaJson,
    ToolRisk risk = ToolRisk.ReadOnly
) : ITool
{
    public string Name { get; } = name;

    public string Description { get; } = description;

    public string SchemaJson { get; } = schemaJson;

    public JsonElement ParametersSchema { get; } = ParseSchema(schemaJson);

    public ToolRisk Risk { get; } = risk;

    public string? ReceivedArgsRaw { get; private set; }

    public ToolContext? ReceivedContext { get; private set; }

    public Func<JsonElement, ToolResult>? OnExecute { get; set; }

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        ReceivedArgsRaw = args.GetRawText();
        ReceivedContext = ctx;
        ToolResult? result = OnExecute?.Invoke(args);
        return Task.FromResult(result ?? new ToolResult($"ran {Name}", IsError: false));
    }

    private static JsonElement ParseSchema(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
