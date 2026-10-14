using System.Globalization;

namespace Lunate.Extensibility;

public sealed partial class ModelRegistry
{
    /// <summary>
    /// Validates the virtual model's chain once (unknown backends, cycles, depth, capability
    /// narrowing) and returns its capabilities: the declared values when present, otherwise the
    /// weakest backend's.
    /// </summary>
    private Capabilities ValidateVirtualChain(VirtualEntry root)
    {
        lock (_gate)
        {
            if (
                _validatedVirtuals.Contains(root.FullId)
                && _capabilities.TryGetValue(root.FullId, out Capabilities? cached)
            )
            {
                return cached;
            }
        }

        Dictionary<string, Capabilities> memo = new(StringComparer.Ordinal);
        List<string> validated = [];
        Capabilities result = WalkVirtual(root, [], memo, validated);
        lock (_gate)
        {
            foreach ((string id, Capabilities capabilities) in memo)
            {
                _capabilities[id] = capabilities;
            }

            foreach (string id in validated)
            {
                _validatedVirtuals.Add(id);
            }
        }

        return result;
    }

    private Capabilities WalkVirtual(
        VirtualEntry entry,
        List<string> ancestors,
        Dictionary<string, Capabilities> memo,
        List<string> validated
    )
    {
        if (memo.TryGetValue(entry.FullId, out Capabilities? known))
        {
            return known;
        }

        lock (_gate)
        {
            if (
                _validatedVirtuals.Contains(entry.FullId)
                && _capabilities.TryGetValue(entry.FullId, out Capabilities? cached)
            )
            {
                return cached;
            }
        }

        if (ancestors.Contains(entry.FullId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Virtual model '{entry.FullId}' routes in a cycle: {Chain([.. ancestors, entry.FullId])}. Fix the backends so the chain ends at a non-virtual model."
            );
        }

        if (ancestors.Count >= MaxVirtualDepth)
        {
            throw new InvalidOperationException(
                $"Virtual model '{entry.FullId}' routes deeper than {MaxVirtualDepth.ToString(CultureInfo.InvariantCulture)}: {Chain([.. ancestors, entry.FullId])}. Flatten the chain."
            );
        }

        List<string> chain = [.. ancestors, entry.FullId];
        int window = int.MaxValue;
        var tools = true;
        string? weakestBackend = null;
        foreach (string backend in entry.Registration.Backends)
        {
            Capabilities capabilities = WalkBackend(entry, backend, chain, memo, validated);
            if (capabilities.Window < window)
            {
                window = capabilities.Window;
                weakestBackend = backend;
            }

            tools &= capabilities.Tools;
        }

        if (
            entry.Registration.ContextWindow is { } declaredWindow
            && window != int.MaxValue
            && declaredWindow > window
        )
        {
            throw new InvalidOperationException(
                $"Virtual model '{entry.FullId}' declares context window {declaredWindow.ToString(CultureInfo.InvariantCulture)} but its weakest backend '{weakestBackend}' supports {window.ToString(CultureInfo.InvariantCulture)}. Declare at most the weakest backend's window."
            );
        }

        if (entry.Registration.SupportsTools is true && !tools)
        {
            throw new InvalidOperationException(
                $"Virtual model '{entry.FullId}' declares supportsTools true but backend '{weakestBackend}' does not support tools. Declare supportsTools false or fix the backend."
            );
        }

        var result = new Capabilities(
            entry.Registration.ContextWindow ?? window,
            entry.Registration.SupportsTools ?? tools
        );
        memo[entry.FullId] = result;
        validated.Add(entry.FullId);
        return result;
    }

    private Capabilities WalkBackend(
        VirtualEntry owner,
        string backendId,
        List<string> chain,
        Dictionary<string, Capabilities> memo,
        List<string> validated
    )
    {
        object? entry = Find(backendId);
        if (entry is null)
        {
            throw new InvalidOperationException(
                $"Virtual model '{owner.FullId}' names backend '{backendId}', which is not a known model. Known models: {KnownModelIds()}. Fix the backend or register the model."
            );
        }

        return entry switch
        {
            VirtualEntry nested => WalkVirtual(nested, chain, memo, validated),
            DeclaredEntry declared => new Capabilities(
                declared.Descriptor.ContextWindow,
                declared.Descriptor.SupportsTools
            ),
            // A programmatic provider declares no capabilities: unknown window (does not narrow
            // the minimum) and assumed tool support; it does not constrain its virtual model.
            ProgrammaticEntry => new Capabilities(int.MaxValue, true),
            CatalogModel catalog => new Capabilities(
                catalog.Info.ContextWindow,
                catalog.Info.SupportsTools
            ),
            _ => throw new InvalidOperationException(
                $"Virtual model '{owner.FullId}' names backend '{backendId}', which has an unknown entry kind; this is a registry bug."
            ),
        };
    }

    private static ModelCapabilities ToPublic(Capabilities capabilities) =>
        new(capabilities.Window == int.MaxValue ? 0 : capabilities.Window, capabilities.Tools);

    private static string Chain(IReadOnlyList<string> ids) => string.Join(" -> ", ids);
}
