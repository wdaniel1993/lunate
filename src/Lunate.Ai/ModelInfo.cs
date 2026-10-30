namespace Lunate.Ai;

/// <summary>
/// A model in the catalog. <see cref="AuthRef"/> optionally names a key in the user's auth store:
/// the deliberate opt-in that lets that key reach a declared (custom) endpoint.
/// <see cref="Reasoning"/> declares the model's reasoning style: <c>effort</c>, <c>budget</c> or
/// <c>inline</c>; null declares no reasoning support.
/// <see cref="Pricing"/> carries optional rates per million tokens; null means the model is
/// unpriced. <see cref="MaxOutput"/> is the optional maximum output token count,
/// <see cref="SupportsVision"/> declares image input support and <see cref="WireId"/> is the
/// upstream id when it differs from <see cref="Id"/> (null sends <see cref="Id"/>).
/// </summary>
public sealed record ModelInfo(
    string Id,
    string Provider,
    Uri? Endpoint,
    int ContextWindow,
    bool SupportsTools,
    string? AuthRef = null,
    string? Reasoning = null
)
{
    public ModelPricing? Pricing { get; init; }

    public int? MaxOutput { get; init; }

    public bool SupportsVision { get; init; }

    /// <summary>Open API, modality, and feature facts; omitted categories remain unknown.</summary>
    public ModelCapabilityDescriptor? Capabilities { get; init; }

    /// <summary>Decision request path label: <c>native</c> or <c>adapter</c>; it is not a gate.</summary>
    public string? ApiType { get; init; }

    internal bool SupportsTemperature { get; init; }

    public string? WireId { get; init; }
}
