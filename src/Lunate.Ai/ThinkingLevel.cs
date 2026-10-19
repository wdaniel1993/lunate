namespace Lunate.Ai;

/// <summary>
/// The session thinking level: how much reasoning a model should do. <see cref="Off"/> sends no
/// provider parameters at all; the mapping to provider options lives in
/// <see cref="ReasoningParameters"/>.
/// </summary>
public enum ThinkingLevel
{
    /// <summary>No reasoning parameters are sent (default).</summary>
    Off,

    /// <summary>A small reasoning budget.</summary>
    Low,

    /// <summary>A medium reasoning budget.</summary>
    Medium,

    /// <summary>A large reasoning budget.</summary>
    High,
}
