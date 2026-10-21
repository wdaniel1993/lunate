namespace Lunate.Ai;

/// <summary>
/// The wire adapter a provider preset uses. <see cref="OpenAi"/> and <see cref="Anthropic"/> are
/// native adapters; <see cref="OpenAiCompatible"/> routes through the OpenAI-compatible adapter.
/// </summary>
public enum ProviderAdapter
{
    OpenAi,
    Anthropic,
    OpenAiCompatible,
}
