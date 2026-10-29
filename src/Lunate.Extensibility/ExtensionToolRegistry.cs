using System.Text.Json;
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
    private const int MaxToolsPerExtension = 32;
    private const int MaxNameLength = Abstractions.ExtensionToolNaming.MaxModelFacingNameLength;

    private readonly object _gate = new();
    private readonly List<ExtensionToolAdapter> _tools = [];
    private readonly Dictionary<string, ExtensionToolAdapter> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _countByExtension = new(StringComparer.Ordinal);

    // The lifetime source is cancelled, never disposed: an adapter may be mid-construction or
    // mid-call while the extension unloads, and a cancelled token keeps working from any snapshot.
    private readonly Dictionary<string, CancellationTokenSource> _lifetimes = new(
        StringComparer.Ordinal
    );

    /// <summary>Registers one tool for the extension; refusals throw naming the extension and field.</summary>
    public void Register(string extensionId, ExtensionToolRegistration registration) =>
        Register(extensionId, registration, activationRule: null);

    /// <summary>
    /// Registers one tool with the manifest's <c>toolActivation</c> rule for it, stamped by the
    /// loader from the cross-checked manifest: the rule rides the adapter so the composition can
    /// filter per run root without touching the registration contract.
    /// </summary>
    internal void Register(
        string extensionId,
        ExtensionToolRegistration registration,
        ExtensionToolActivationRule? activationRule
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(registration);
        Validate(extensionId, registration);

        lock (_gate)
        {
            string name = ExtensionToolNaming.ModelFacing(extensionId, registration.Name);
            if (_byName.TryGetValue(name, out ExtensionToolAdapter? existing))
            {
                throw new InvalidOperationException(
                    $"extension '{extensionId}': tool '{registration.Name}' was refused: the model-facing name '{name}' is already registered by extension '{existing.ExtensionId}' tool '{existing.LocalName}'."
                );
            }

            if (_countByExtension.GetValueOrDefault(extensionId) >= MaxToolsPerExtension)
            {
                throw new InvalidOperationException(
                    $"extension '{extensionId}': tool '{registration.Name}' was refused: the limit of {MaxToolsPerExtension} tools per extension is reached."
                );
            }

            var tool = new ExtensionToolAdapter(
                extensionId,
                registration,
                Lifetime(extensionId).Token,
                activationRule
            );
            _tools.Add(tool);
            _byName[name] = tool;
            _countByExtension[extensionId] = _countByExtension.GetValueOrDefault(extensionId) + 1;
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
                    _byName.Remove(_tools[index].Name);
                    _tools.RemoveAt(index);
                }
                else
                {
                    index++;
                }
            }

            if (removed.Count > 0)
            {
                _countByExtension.Remove(extensionId);
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

    private static void Validate(string extensionId, ExtensionToolRegistration registration)
    {
        string name = registration.Name ?? string.Empty;
        if (!Abstractions.ExtensionToolNaming.IsValidLocalName(name))
        {
            throw Refused(
                extensionId,
                name,
                "field 'name' must be a non-empty name matching [A-Za-z0-9_-]."
            );
        }

        string modelFacing = Abstractions.ExtensionToolNaming.ModelFacing(extensionId, name);
        if (modelFacing.Length > MaxNameLength)
        {
            throw Refused(
                extensionId,
                name,
                $"field 'name' makes the model-facing name '{modelFacing}' longer than {MaxNameLength} characters."
            );
        }

        if (string.IsNullOrWhiteSpace(registration.Description))
        {
            throw Refused(
                extensionId,
                name,
                "field 'description' must be a non-empty description."
            );
        }

        if (registration.ParametersSchema.ValueKind != JsonValueKind.Object)
        {
            throw Refused(extensionId, name, "field 'parametersSchema' must be a JSON object.");
        }

        if (
            registration.OutputSchema is
            { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) }
        )
        {
            throw Refused(extensionId, name, "field 'outputSchema' must be a JSON object.");
        }

        if (registration.Handler is null)
        {
            throw Refused(extensionId, name, "field 'handler' is required.");
        }

        if (!Enum.IsDefined(registration.Exposure))
        {
            throw Refused(extensionId, name, "field 'exposure' has an unknown value.");
        }
    }

    private static InvalidOperationException Refused(
        string extensionId,
        string name,
        string reason
    ) =>
        new(
            name.Length == 0
                ? $"extension '{extensionId}': tool registration was refused: {reason}"
                : $"extension '{extensionId}': tool '{name}' was refused: {reason}"
        );
}
