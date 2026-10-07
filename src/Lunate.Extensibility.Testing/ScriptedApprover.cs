using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Extensibility.Testing;

/// <summary>
/// A tool approver driven by a fixed queue of decisions. Exhaustion throws: a test that expected no
/// approval must be fixed, never allowed implicitly. Decision and call counts are exposed.
/// </summary>
public sealed class ScriptedApprover : IToolApprover
{
    private readonly Queue<bool> _decisions;

    public ScriptedApprover(params bool[] decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        _decisions = new Queue<bool>(decisions);
    }

    public int Calls { get; private set; }

    public int Approvals { get; private set; }

    public int Denials { get; private set; }

    public ValueTask<bool> ApproveAsync(ITool tool, JsonElement args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tool);
        Calls++;
        if (!_decisions.TryDequeue(out bool decision))
        {
            throw new InvalidOperationException(
                $"ScriptedApprover has no decision left for tool '{tool.Name}' (call {Calls}). Queue one decision per expected approval request; exhaustion never allows the call implicitly."
            );
        }

        if (decision)
        {
            Approvals++;
        }
        else
        {
            Denials++;
        }

        return ValueTask.FromResult(decision);
    }
}
