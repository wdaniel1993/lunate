using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

/// <summary>
/// The decision-model resolution every coding frontend shares: the configured
/// <c>decisions.profile</c> resolves into the <c>UsageModels["decision"]</c> client the approval
/// gate, the <c>decide</c> tool and the routing consumer read. Print mode and ACP weave it like
/// the interactive session, because the fail-closed approval rule covers every mode. A profile
/// that cannot resolve there — a missing provider, an unavailable credential — degrades to no
/// gate with a visible warning, not a crash.
/// </summary>
internal static class DecisionUsageModels
{
    /// <summary>
    /// Resolves the configured <c>decisions.profile</c> into the decision usage client, or null
    /// when the layer is unconfigured or the profile cannot resolve. Each resolution failure
    /// warns once through <paramref name="warn"/> (deduplicated by <paramref name="warned"/>);
    /// the sources function loads the credential store and the provider entries, lazily, so a
    /// frontend that never configures the decision layer touches neither.
    /// </summary>
    internal static UsageModelClient? ResolveDecisionModel(
        AgentSettings settings,
        Func<(AuthStore Auth, IReadOnlyList<ProviderEntry> Providers)> sources,
        ModelCatalog catalog,
        Func<string, string?> environment,
        IChatClientFactory factory,
        Action<UserNotice> warn,
        HashSet<string> warned
    )
    {
        if (settings.Decisions.Profile is not { } profile)
        {
            return null;
        }

        try
        {
            (AuthStore auth, IReadOnlyList<ProviderEntry> providers) = sources();
            ResolvedModelProfile resolved = ModelSelectionResolver.ResolveChatModel(
                profile,
                settings,
                providers,
                catalog
            );
            return BuildClient(resolved, auth, environment, factory);
        }
        catch (Exception exception)
            when (exception
                    is UnknownModelException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            if (warned.Add(profile))
            {
                warn(
                    new UserNotice(
                        NoticeLevel.Warning,
                        $"decision profile '{profile}' is unavailable; the decision layer is off: {exception.Message}"
                    )
                );
            }

            return null;
        }
    }

    /// <summary>
    /// Resolves the routing profiles the routing consumer selects between; empty or unresolved
    /// names are a settings-validation error before this point, so a failure here surfaces as a
    /// composition error rather than a degraded gate.
    /// </summary>
    internal static IReadOnlyDictionary<string, UsageModelClient>? ResolveRoutingProfiles(
        AgentSettings settings,
        IReadOnlyList<ProviderEntry> providers,
        AuthStore auth,
        ModelCatalog catalog,
        Func<string, string?> environment,
        IChatClientFactory factory
    )
    {
        if (settings.Decisions.Routing == DecisionMode.Off)
        {
            return null;
        }

        var profiles = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal);
        foreach (string profileName in settings.Decisions.RoutingProfiles)
        {
            ResolvedModelProfile profile = ModelSelectionResolver.ResolveChatModel(
                profileName,
                settings,
                providers,
                catalog
            );
            profiles.Add(profileName, BuildClient(profile, auth, environment, factory));
        }

        return profiles;
    }

    private static UsageModelClient BuildClient(
        ResolvedModelProfile profile,
        AuthStore auth,
        Func<string, string?> environment,
        IChatClientFactory factory
    ) =>
        new(
            new DeferredChatClient(() => CreateUsageClient(profile, auth, environment, factory)),
            profile.Profile.Name,
            profile.Provider.Name,
            profile.Model
        );

    /// <summary>
    /// Materializes a profile's client from its credential reference: an environment or stored key
    /// when it resolves, otherwise a deferred factory call that fails at first use with the
    /// provider's message.
    /// </summary>
    private static IChatClient CreateUsageClient(
        ResolvedModelProfile profile,
        AuthStore auth,
        Func<string, string?> environment,
        IChatClientFactory factory
    )
    {
        string reference = profile.Provider.CredentialReference;
        if (reference.StartsWith("auth:", StringComparison.Ordinal))
        {
            string keyName = reference["auth:".Length..];
            ProviderPreset? preset = ProviderPresets.Find(profile.Provider.Kind);
            string? environmentKey = preset is null ? null : environment(preset.KeyVariable);
            if (!string.IsNullOrWhiteSpace(environmentKey))
            {
                return factory.Create(profile.Model, environmentKey);
            }

            string? storedKey = auth.TryGet(keyName);
            if (!string.IsNullOrWhiteSpace(storedKey))
            {
                return factory.Create(profile.Model, storedKey);
            }

            return factory.Create(profile.Model with { AuthRef = keyName });
        }

        if (reference.StartsWith("env:", StringComparison.Ordinal))
        {
            string variable = reference["env:".Length..];
            string? environmentKey = environment(variable);
            if (!string.IsNullOrWhiteSpace(environmentKey))
            {
                return factory.Create(profile.Model, environmentKey);
            }

            throw new InvalidOperationException(
                $"The environment credential '{variable}' for provider '{profile.Provider.Name}' is not set. Set {variable} before using this profile."
            );
        }

        throw new InvalidOperationException(
            $"The credential helper '{reference["helper:".Length..]}' for provider '{profile.Provider.Name}' cannot be resolved by this host. Use an auth or env credential reference."
        );
    }
}
