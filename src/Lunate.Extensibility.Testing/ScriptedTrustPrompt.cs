using System.Globalization;
using Lunate.Extensibility;

namespace Lunate.Extensibility.Testing;

/// <summary>
/// A trust prompt driven by a fixed queue of decisions. Exhaustion throws: a test that expected no
/// prompt must be fixed, never approved implicitly. Decision and call counts are exposed.
/// </summary>
public sealed class ScriptedTrustPrompt : IExtensionTrustPrompt
{
    private readonly Queue<bool> _decisions;

    public ScriptedTrustPrompt(params bool[] decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        _decisions = new Queue<bool>(decisions);
    }

    public int Calls { get; private set; }

    public int Approvals { get; private set; }

    public int Denials { get; private set; }

    public ValueTask<bool> ApproveAsync(
        ExtensionDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Calls++;
        if (!_decisions.TryDequeue(out bool decision))
        {
            throw new InvalidOperationException(
                $"ScriptedTrustPrompt has no decision left for extension '{descriptor.Id}' (call {Calls.ToString(CultureInfo.InvariantCulture)}). Queue one decision per expected trust prompt; exhaustion never approves implicitly."
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
