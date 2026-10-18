using System.Globalization;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// The welcome header's first-run check (design D4): whether a credential resolves for the model
/// the session runs, following the client factory's precedence — a declared endpoint uses the
/// placeholder credential and needs no key; without one the provider's environment variable, then
/// the auth store's key under the provider name, resolves it. When nothing resolves the probe
/// answers with exactly one actionable fix, never a list of alternatives: the name the auth store
/// wants, or the environment variable that is set but does not resolve.
/// </summary>
internal static class ProviderKeyProbe
{
    private const string OpenAiProvider = "openai";
    private const string AnthropicProvider = "anthropic";
    private const string OpenAiApiKeyVariable = "OPENAI_API_KEY";
    private const string AnthropicApiKeyVariable = "ANTHROPIC_API_KEY";
    private const string AuthFileName = "~/.lunate/auth.json";

    /// <summary>The one fix for an unresolved credential, or null when nothing needs resolving.</summary>
    internal static string? FixFor(
        ModelInfo model,
        AuthStore auth,
        Func<string, string?> environment
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(environment);

        if (model.Endpoint is not null)
        {
            // A declared endpoint gets the placeholder credential: local servers need no key.
            return null;
        }

        if (!string.IsNullOrEmpty(model.AuthRef))
        {
            // The named reference's stored key is the fix; the environment cannot carry it.
            return string.IsNullOrEmpty(auth.TryGet(model.AuthRef))
                ? MissingKeyFix(model.AuthRef)
                : null;
        }

        if (VariableFor(model.Provider) is not { } variable)
        {
            // An unknown provider has no credential advice; the client creation fails instead.
            return null;
        }

        string? value = environment(variable);
        if (!string.IsNullOrEmpty(value))
        {
            return string.IsNullOrWhiteSpace(value)
                ? FormattableString.Invariant($"set {variable} to a valid key")
                : null;
        }

        return string.IsNullOrEmpty(auth.TryGet(model.Provider))
            ? MissingKeyFix(model.Provider)
            : null;
    }

    private static string MissingKeyFix(string name) => $"add the '{name}' key to {AuthFileName}";

    private static string? VariableFor(string provider) =>
        provider.ToLower(CultureInfo.InvariantCulture) switch
        {
            OpenAiProvider => OpenAiApiKeyVariable,
            AnthropicProvider => AnthropicApiKeyVariable,
            _ => null,
        };
}
