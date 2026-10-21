using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// The welcome header's first-run check (design D4): whether a credential resolves for the model
/// the session runs, following the client factory's precedence — an explicit auth reference wins;
/// a foreign declared endpoint uses the placeholder credential and needs no key; otherwise the
/// preset's environment variable (or its alias), then the auth store's key under the preset id,
/// resolves it. When nothing resolves the probe answers with exactly one actionable fix, never a
/// list of alternatives: the name the auth store wants, or the environment variable that is set
/// but does not resolve. A session with no model configured at all has nothing to probe and uses
/// <see cref="MissingModelFix"/>.
/// </summary>
internal static class ProviderKeyProbe
{
    /// <summary>
    /// The one fix when nothing is configured at all: the first action of the shipped resolution
    /// error (<c>HarnessFactory.ResolveModel</c>), kept to one line and one action (design D4/D8 #9).
    /// </summary>
    internal const string MissingModelFix = "set 'model' in ~/.lunate/settings.json";

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

        if (!string.IsNullOrWhiteSpace(model.AuthRef))
        {
            // The named reference's stored key is the fix; the environment cannot carry it.
            return string.IsNullOrWhiteSpace(auth.TryGet(model.AuthRef))
                ? MissingKeyFix(model.AuthRef)
                : null;
        }

        if (ProviderPresets.Find(model.Provider) is not { } preset)
        {
            // An unknown provider has no credential advice; the client creation fails instead.
            return null;
        }

        if (model.Endpoint is not null && !ProviderEndpoint.IsOwn(model.Endpoint, preset.BaseUrl))
        {
            // A foreign endpoint gets the placeholder credential: local servers need no key.
            return null;
        }

        string? value = environment(preset.KeyVariable);
        string? aliasValue = preset.KeyVariableAlias is { } alias ? environment(alias) : null;
        bool blankValueWasSet = !string.IsNullOrEmpty(value) || !string.IsNullOrEmpty(aliasValue);
        if (!string.IsNullOrWhiteSpace(value) || !string.IsNullOrWhiteSpace(aliasValue))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(auth.TryGet(preset.Id)))
        {
            return null;
        }

        return blankValueWasSet
            ? FormattableString.Invariant($"set {preset.KeyVariable} to a valid key")
            : MissingKeyFix(preset.Id);
    }

    private static string MissingKeyFix(string name) => $"add the '{name}' key to {AuthFileName}";
}
