using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The tools extensions registered, in registration order, with one cancellation lifetime per
/// extension. Registration is registration only; the composition maps the adapters into the
/// session's tool registry. Unregistering an extension removes its tools and cancels its in-flight
/// calls — a cancelled call settles as a cancelled result on the adapter (design D8).
/// </summary>
public sealed class ExtensionToolRegistry
{
    private readonly object _gate = new();
    private readonly List<ExtensionToolAdapter> _tools = [];

    // The lifetime source is cancelled, never disposed: an adapter may be mid-construction or
    // mid-call while the extension unloads, and a cancelled token keeps working from any snapshot.
    private readonly Dictionary<string, CancellationTokenSource> _lifetimes = new(
        StringComparer.Ordinal
    );

    /// <summary>Registers one tool for the extension; refusals throw naming the extension and field.</summary>
    public void Register(string extensionId, ExtensionToolRegistration registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(registration);

        lock (_gate)
        {
            _tools.Add(
                new ExtensionToolAdapter(extensionId, registration, Lifetime(extensionId).Token)
            );
        }
    }

    /// <summary>The registered tools in registration order.</summary>
    public IReadOnlyList<ExtensionToolAdapter> List()
    {
        lock (_gate)
        {
            return [.. _tools];
        }
    }

    /// <summary>
    /// Removes every tool the extension registered, in declaration order, and cancels its in-flight
    /// calls; returns the removed model-facing names.
    /// </summary>
    public IReadOnlyList<string> Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        lock (_gate)
        {
            List<string> removed = [];
            for (var index = 0; index < _tools.Count; )
            {
                if (string.Equals(_tools[index].ExtensionId, extensionId, StringComparison.Ordinal))
                {
                    removed.Add(_tools[index].Name);
                    _tools.RemoveAt(index);
                }
                else
                {
                    index++;
                }
            }

            if (_lifetimes.Remove(extensionId, out CancellationTokenSource? lifetime))
            {
                lifetime.Cancel();
            }

            return removed;
        }
    }

    private CancellationTokenSource Lifetime(string extensionId)
    {
        if (!_lifetimes.TryGetValue(extensionId, out CancellationTokenSource? lifetime))
        {
            lifetime = new CancellationTokenSource();
            _lifetimes[extensionId] = lifetime;
        }

        return lifetime;
    }
}

/// <summary>The naming grammar of extension tools: the literal MCP-style double underscore prefix.</summary>
internal static class ExtensionToolNaming
{
    internal static string ModelFacing(string extensionId, string localName) =>
        $"{extensionId}__{localName}";
}
