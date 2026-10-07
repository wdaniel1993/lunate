using System.Text.Json;

namespace Lunate.Agent.Tests;

/// <summary>A tool that relies on the interface defaults, including the derived risk.</summary>
internal sealed class ContractTool(string name = "tool") : ITool
{
    public string Name { get; } = name;

    public string Description => "Relies on the ITool defaults.";

    public JsonElement ParametersSchema { get; } = ParseSchema();

    public ToolExposure Exposure { get; init; } = ToolExposure.Direct;

    public ToolNamespace? Namespace { get; init; }

    public ToolAnnotations? Annotations { get; init; }

    public JsonElement? OutputSchema { get; init; }

    public ToolConcurrency Concurrency { get; init; } = ToolConcurrency.Parallel;

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct) =>
        Task.FromResult(new ToolResult($"ran {Name}", IsError: false));

    private static JsonElement ParseSchema()
    {
        using JsonDocument document = JsonDocument.Parse("""{"type":"object"}""");
        return document.RootElement.Clone();
    }
}
