using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private IReadOnlyDictionary<string, UsageModelClient>? ResolveUsageModels(
        ModelInfo fallbackChat
    )
    {
        if (_extensionModelId is not null)
        {
            return null;
        }

        string chatSelection = _settings.Usages.GetValueOrDefault("chat") ?? fallbackChat.Id;
        ResolvedModelProfile chat = ModelSelectionResolver.ResolveChatModel(
            chatSelection,
            _settings,
            _providerEntries,
            _catalog
        );
        var clientsByProfile = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal);
        var usages = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
        {
            ["chat"] = GetClient(chat),
        };

        foreach ((string usage, string profileName) in _settings.Usages)
        {
            if (usage == "chat")
            {
                continue;
            }

            ResolvedModelProfile profile = ModelSelectionResolver.ResolveChatModel(
                profileName,
                _settings,
                _providerEntries,
                _catalog
            );
            usages[usage] = GetClient(profile);
        }

        // The decision profile is configured in the decisions block, not as a usage.
        if (_settings.Decisions.Profile is { } decisionProfile)
        {
            usages["decision"] = GetClient(
                ModelSelectionResolver.ResolveChatModel(
                    decisionProfile,
                    _settings,
                    _providerEntries,
                    _catalog
                )
            );
        }

        return usages;

        UsageModelClient GetClient(ResolvedModelProfile profile)
        {
            string key = profile.Profile.Name;
            if (clientsByProfile.TryGetValue(key, out UsageModelClient? existing))
            {
                return existing;
            }

            var client = new UsageModelClient(
                new DeferredChatClient(() => CreateUsageClient(profile)),
                profile.Profile.Name,
                profile.Provider.Name,
                profile.Model
            );
            clientsByProfile.Add(key, client);
            return client;
        }
    }

    private IReadOnlyDictionary<string, UsageModelClient>? ResolveRoutingProfiles()
    {
        if (_settings.Decisions.Routing == DecisionMode.Off)
        {
            return null;
        }

        var profiles = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal);
        foreach (string profileName in _settings.Decisions.RoutingProfiles)
        {
            ResolvedModelProfile profile = ModelSelectionResolver.ResolveChatModel(
                profileName,
                _settings,
                _providerEntries,
                _catalog
            );
            profiles.Add(
                profileName,
                new UsageModelClient(
                    new DeferredChatClient(() => CreateUsageClient(profile)),
                    profile.Profile.Name,
                    profile.Provider.Name,
                    profile.Model
                )
            );
        }

        return profiles;
    }

    private IChatClient CreateUsageClient(ResolvedModelProfile profile)
    {
        string reference = profile.Provider.CredentialReference;
        if (reference.StartsWith("auth:", StringComparison.Ordinal))
        {
            string keyName = reference["auth:".Length..];
            ProviderPreset? preset = ProviderPresets.Find(profile.Provider.Kind);
            string? environmentKey = preset is null
                ? null
                : _environment(preset.KeyVariable)
                    ?? (preset.KeyVariableAlias is { } alias ? _environment(alias) : null);
            if (!string.IsNullOrWhiteSpace(environmentKey))
            {
                return _factory.Create(profile.Model, environmentKey);
            }

            string? storedKey = _auth.TryGet(keyName);
            if (!string.IsNullOrWhiteSpace(storedKey))
            {
                return _factory.Create(profile.Model, storedKey);
            }

            return _factory.Create(profile.Model with { AuthRef = keyName });
        }

        if (reference.StartsWith("env:", StringComparison.Ordinal))
        {
            string variable = reference["env:".Length..];
            string? environmentKey = _environment(variable);
            if (!string.IsNullOrWhiteSpace(environmentKey))
            {
                return _factory.Create(profile.Model, environmentKey);
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
