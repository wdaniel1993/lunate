using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using AgentToolExposure = Lunate.Agent.ToolExposure;

namespace Lunate.Extensibility;

/// <summary>
/// One registered extension tool mapped onto the core <see cref="ITool"/> model: the model-facing
/// name is <c>&lt;extension-id&gt;__&lt;name&gt;</c>, the namespace is the extension id, every other
/// field passes through, and the handler receives the real <see cref="ToolContext"/>. Each call is
/// linked to the extension's lifetime, so an unload cancels the call and it settles as a cancelled
/// result instead of an exception (design D7, D8).
/// </summary>
public sealed class ExtensionToolAdapter : ITool
{
    private readonly ExtensionToolRegistration _registration;
    private readonly CancellationToken _lifetime;

    internal ExtensionToolAdapter(
        string extensionId,
        ExtensionToolRegistration registration,
        CancellationToken lifetime,
        ExtensionToolActivationRule? activationRule
    )
    {
        ExtensionId = extensionId;
        _registration = registration;
        _lifetime = lifetime;
        ActivationRule = activationRule;
        Name = ExtensionToolNaming.ModelFacing(extensionId, registration.Name);
    }

    /// <summary>The extension that registered the tool; also its namespace name.</summary>
    public string ExtensionId { get; }

    /// <summary>
    /// The manifest's <c>toolActivation</c> rule for this tool, or null when the manifest declared
    /// none — null is always active. Stamped by the loader at registration; the composition
    /// evaluates it once per run root and filters the tool out of the offered set when it is false.
    /// </summary>
    internal ExtensionToolActivationRule? ActivationRule { get; }

    /// <summary>The registration's local name, as the manifest declares it.</summary>
    public string LocalName => _registration.Name;

    public string Name { get; }

    public string Description => _registration.Description;

    public JsonElement ParametersSchema => _registration.ParametersSchema;

    public AgentToolExposure Exposure => _registration.Exposure;

    public ToolNamespace? Namespace => new(ExtensionId);

    public ToolAnnotations? Annotations => _registration.Annotations;

    public JsonElement? OutputSchema => _registration.OutputSchema;

    public ToolRisk Risk =>
        _registration.Risk ?? ToolRiskResolver.FromAnnotations(_registration.Annotations);

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            _lifetime
        );
        try
        {
            return await _registration
                .Handler.InvokeAsync(args, ctx, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (_lifetime.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new ToolResult(
                $"Tool '{Name}' was cancelled because extension '{ExtensionId}' was unloaded. The call was not completed; do not retry it unless the extension is loaded again.",
                IsError: true
            );
        }
    }
}
