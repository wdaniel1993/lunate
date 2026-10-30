using Lunate.Ai;

namespace Lunate.Coding;

internal static class ModelSelectionResolver
{
    public static ResolvedModelProfile ResolveChatModel(
        string selection,
        AgentSettings settings,
        IReadOnlyList<ProviderEntry> providers,
        ModelCatalog catalog
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selection);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(catalog);

        if (settings.Profiles.TryGetValue(selection, out ModelProfile? configuredProfile))
        {
            return ModelProfileResolver.Resolve(configuredProfile, providers, catalog);
        }

        ModelInfo model;
        ProviderEntry? provider = FindQualifiedProvider(selection, providers);
        if (provider is not null)
        {
            string modelId = selection[(selection.IndexOf('/', StringComparison.Ordinal) + 1)..];
            model = FindQualifiedModel(provider, modelId, catalog, selection);
        }
        else
        {
            model = FindModel(selection, catalog);
            provider = FindProvider(model.Provider, providers);
        }

        string profileName = SelectionProfileName(provider.Name, model.Id, settings.Profiles);
        var profile = new ModelProfile(profileName, provider.Name, model.Id);
        return ModelProfileResolver.Resolve(profile, [provider], catalog);
    }

    public static AgentSettings SelectChatProfile(
        AgentSettings settings,
        ResolvedModelProfile selected
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(selected);

        var profiles = new Dictionary<string, ModelProfile>(
            settings.Profiles,
            StringComparer.Ordinal
        )
        {
            [selected.Profile.Name] = selected.Profile,
        };
        var usages = new Dictionary<string, string>(settings.Usages, StringComparer.Ordinal)
        {
            ["chat"] = selected.Profile.Name,
        };
        return settings with { Profiles = profiles, Usages = usages };
    }

    private static ProviderEntry? FindQualifiedProvider(
        string selection,
        IReadOnlyList<ProviderEntry> providers
    )
    {
        int slash = selection.IndexOf('/', StringComparison.Ordinal);
        return slash > 0
            ? providers.SingleOrDefault(provider =>
                string.Equals(provider.Name, selection[..slash], StringComparison.Ordinal)
            )
            : null;
    }

    private static ModelInfo FindQualifiedModel(
        ProviderEntry provider,
        string modelId,
        ModelCatalog catalog,
        string selection
    ) =>
        catalog.Find($"{provider.Kind}/{modelId}")
        ?? catalog.Find(modelId)
        ?? throw UnknownModel(selection);

    private static ModelInfo FindModel(string selection, ModelCatalog catalog)
    {
        ModelInfo? direct = catalog.Find(selection);
        if (direct is not null)
        {
            return direct;
        }

        ModelInfo[] candidates =
        [
            .. catalog.Models.Where(model =>
                string.Equals(LastSegment(model.Id), selection, StringComparison.Ordinal)
                || (
                    model.WireId is { } wireId
                    && string.Equals(LastSegment(wireId), selection, StringComparison.Ordinal)
                )
            ),
        ];
        return candidates switch
        {
            [ModelInfo model] => model,
            [] => throw UnknownModel(selection),
            _ => throw new InvalidOperationException(
                $"Model id '{selection}' is ambiguous: {string.Join(", ", candidates.Select(model => model.Id))}. Use the qualified id."
            ),
        };
    }

    private static string LastSegment(string id)
    {
        int slash = id.LastIndexOf('/');
        return slash < 0 ? id : id[(slash + 1)..];
    }

    private static ProviderEntry FindProvider(string kind, IReadOnlyList<ProviderEntry> providers)
    {
        ProviderEntry[] matches = [.. providers.Where(provider => provider.Kind == kind)];
        return matches switch
        {
            [ProviderEntry provider] => provider,
            [] => DefaultProvider(kind),
            _ => throw new InvalidOperationException(
                $"Provider kind '{kind}' has multiple configured entries: {string.Join(", ", matches.Select(provider => provider.Name))}. Use a qualified provider/model id."
            ),
        };
    }

    private static ProviderEntry DefaultProvider(string kind)
    {
        ProviderPreset preset =
            ProviderPresets.Find(kind)
            ?? throw new InvalidOperationException($"Unknown provider kind '{kind}'.");
        return new ProviderEntry(preset.Id, preset.Id, preset.BaseUrl, $"auth:{preset.Id}");
    }

    private static string SelectionProfileName(
        string provider,
        string model,
        IReadOnlyDictionary<string, ModelProfile> existing
    )
    {
        string prefix = $"selection-{provider}-{model.Replace('/', '-')}";
        string candidate = prefix;
        for (int suffix = 2; existing.ContainsKey(candidate); suffix++)
        {
            candidate =
                $"{prefix}-{suffix.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        return candidate;
    }

    private static UnknownModelException UnknownModel(string selection) =>
        new($"Model '{selection}' is not in the catalog.");
}
