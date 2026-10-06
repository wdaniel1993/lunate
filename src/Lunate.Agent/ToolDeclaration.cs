using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// Presents an <see cref="ITool"/> to Microsoft.Extensions.AI as a declaration only: the name,
/// description and schema are the tool's own, and every invocation path throws — the loop invokes
/// <see cref="ITool.ExecuteAsync"/> directly (ADR-0003). The schema is re-parsed from the tool's
/// raw JSON text into a declaration-owned clone, so the model sees exactly the authored bytes.
/// </summary>
internal sealed class ToolDeclaration : AIFunction
{
    private readonly JsonElement _jsonSchema;

    public ToolDeclaration(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        Tool = tool;
        using JsonDocument document = JsonDocument.Parse(tool.ParametersSchema.GetRawText());
        _jsonSchema = document.RootElement.Clone();
    }

    /// <summary>The wrapped tool, for the loop's direct invocation path.</summary>
    internal ITool Tool { get; }

    public override string Name => Tool.Name;

    public override string Description => Tool.Description;

    public override JsonElement JsonSchema => _jsonSchema;

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken
    ) =>
        throw new NotSupportedException(
            $"Tool '{Tool.Name}' cannot be invoked through Microsoft.Extensions.AI (ADR-0003); "
                + "the agent loop calls ITool.ExecuteAsync directly."
        );
}
