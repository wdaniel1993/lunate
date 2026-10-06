using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Spike.Shared;

public enum SpikeToolRisk
{
    ReadOnly,
    Write,
    Execute,
}

public sealed record SpikeTool(
    string Name,
    string Description,
    string ParametersSchemaJson,
    SpikeToolRisk Risk,
    Func<JsonElement, string> Stub
)
{
    public static SpikeTool Read { get; } =
        new(
            "read",
            "Read a file from the workspace and return numbered lines.",
            """{"type":"object","properties":{"path":{"type":"string"},"offset":{"type":"integer"},"limit":{"type":"integer"}},"required":["path"],"additionalProperties":false}""",
            SpikeToolRisk.ReadOnly,
            args =>
                $"     1  public class Calculator\n     2  {{\n     3      public int Add(int a, int b) => a - b;\n[stub read: {args.GetProperty("path").GetString()}]"
        );

    public static SpikeTool Write { get; } =
        new(
            "write",
            "Write a file, replacing its contents.",
            """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"],"additionalProperties":false}""",
            SpikeToolRisk.Write,
            args => $"wrote 3 lines to {args.GetProperty("path").GetString()} (created)"
        );

    public static SpikeTool Edit { get; } =
        new(
            "edit",
            "Replace old_text with new_text in a file.",
            """{"type":"object","properties":{"path":{"type":"string"},"old_text":{"type":"string"},"new_text":{"type":"string"},"start_line":{"type":"integer"}},"required":["path","old_text","new_text"],"additionalProperties":false}""",
            SpikeToolRisk.Write,
            args => $"edited {args.GetProperty("path").GetString()} lines 3-3 (match: exact)"
        );

    public static SpikeTool Bash { get; } =
        new(
            "bash",
            "Run a shell command in the workspace.",
            """{"type":"object","properties":{"command":{"type":"string"},"timeout_s":{"type":"integer"}},"required":["command"],"additionalProperties":false}""",
            SpikeToolRisk.Execute,
            args => $"[stub bash] {args.GetProperty("command").GetString()} -> exit 0"
        );

    public static IReadOnlyList<SpikeTool> All { get; } = [Read, Write, Edit, Bash];

    public SpikeToolDeclaration Declare() => new(this);
}

public sealed class SpikeToolDeclaration(SpikeTool tool) : AIFunctionDeclaration
{
    public override string Name { get; } = tool.Name;

    public override string Description { get; } = tool.Description;

    public override JsonElement JsonSchema { get; } =
        JsonDocument.Parse(tool.ParametersSchemaJson).RootElement.Clone();
}

public sealed class SpikeAIFunction(SpikeTool tool) : AIFunction
{
    public override string Name { get; } = tool.Name;

    public override string Description { get; } = tool.Description;

    public override JsonElement JsonSchema { get; } =
        JsonDocument.Parse(tool.ParametersSchemaJson).RootElement.Clone();

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        JsonElement args = JsonSerializer.SerializeToElement(arguments);
        return ValueTask.FromResult<object?>(tool.Stub(args));
    }
}
