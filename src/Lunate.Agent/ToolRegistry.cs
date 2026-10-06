using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// The tools of one agent, in registration order. Duplicate names fail fast: MCP tools are
/// prefixed before registration (T-29), so a collision is a wiring bug, not a feature.
/// <see cref="Declarations"/> is what the loop feeds into <c>ChatOptions</c>.
/// </summary>
public sealed class ToolRegistry
{
    private readonly List<ITool> _tools = [];
    private readonly List<AIFunction> _declarations = [];
    private readonly Dictionary<string, ITool> _toolsByName = new(StringComparer.Ordinal);

    /// <summary>The registered tools, in registration order.</summary>
    public IReadOnlyList<ITool> Tools => _tools;

    /// <summary>The declaration adapters, in registration order.</summary>
    public IReadOnlyList<AIFunction> Declarations => _declarations;

    /// <summary>Registers a tool; a duplicate name throws immediately.</summary>
    public void Add(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!_toolsByName.TryAdd(tool.Name, tool))
        {
            throw new ArgumentException(
                $"A tool named '{tool.Name}' is already registered.",
                nameof(tool)
            );
        }

        _tools.Add(tool);
        _declarations.Add(new ToolDeclaration(tool));
    }

    /// <summary>Returns the tool with the given name, or null when none is registered.</summary>
    public ITool? Find(string name) => _toolsByName.GetValueOrDefault(name);
}
