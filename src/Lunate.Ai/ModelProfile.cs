namespace Lunate.Ai;

/// <summary>A named model choice and the optional settings validated against its catalog model.</summary>
internal sealed record ModelProfile(
    string Name,
    string Provider,
    string Model,
    string? ReasoningLevel = null,
    int? ReasoningBudget = null,
    int? MaxOutput = null,
    decimal? Temperature = null,
    int? ContextWindow = null,
    double? DecisionThreshold = null
);

/// <summary>A profile resolved to one user provider entry and one catalog model.</summary>
internal sealed record ResolvedModelProfile(
    ModelProfile Profile,
    ProviderEntry Provider,
    ModelInfo Model,
    int ContextWindow
);

/// <summary>Resolves and validates a profile before any model client can be constructed.</summary>
internal static class ModelProfileResolver
{
    public static ResolvedModelProfile Resolve(
        ModelProfile profile,
        IReadOnlyList<ProviderEntry> providers,
        ModelCatalog catalog
    )
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(catalog);

        ProviderEntry? provider = providers.SingleOrDefault(entry =>
            string.Equals(entry.Name, profile.Provider, StringComparison.Ordinal)
        );
        if (provider is null)
        {
            throw Invalid(profile, "provider", "does not name a configured provider");
        }

        ModelInfo? model;
        try
        {
            model = catalog.Find(profile.Model);
        }
        catch (InvalidOperationException exception)
        {
            throw Invalid(profile, "model", exception.Message);
        }

        if (model is null)
        {
            throw Invalid(profile, "model", $"'{profile.Model}' is not in the model catalog");
        }

        if (!string.Equals(model.Provider, provider.Kind, StringComparison.Ordinal))
        {
            throw Invalid(
                profile,
                "provider",
                $"'{provider.Name}' has kind '{provider.Kind}', but model '{model.Id}' belongs to '{model.Provider}'"
            );
        }

        ValidateReasoning(profile, model);
        ValidateOutput(profile, model);
        ValidateTemperature(profile, model);
        int contextWindow = ValidateContextWindow(profile, model);
        ValidateDecisionThreshold(profile);
        return new ResolvedModelProfile(profile, provider, model, contextWindow);
    }

    private static void ValidateReasoning(ModelProfile profile, ModelInfo model)
    {
        if (profile.ReasoningLevel is { } level)
        {
            if (level is not ("low" or "medium" or "high"))
            {
                throw Invalid(profile, "reasoningLevel", "must be low, medium, or high");
            }

            if (model.Reasoning is not ("effort" or "budget"))
            {
                throw Invalid(
                    profile,
                    "reasoningLevel",
                    $"model '{model.Id}' has no level control"
                );
            }
        }

        if (profile.ReasoningBudget is int budget)
        {
            if (
                budget <= 0
                || model.Reasoning != "budget"
                || model.MaxOutput is not int maximum
                || budget > maximum
            )
            {
                throw Invalid(
                    profile,
                    "reasoningBudget",
                    $"model '{model.Id}' does not support a positive token budget up to its maximum output"
                );
            }

            if (profile.ReasoningLevel is not null)
            {
                throw Invalid(profile, "reasoningBudget", "cannot be combined with reasoningLevel");
            }
        }
    }

    private static void ValidateOutput(ModelProfile profile, ModelInfo model)
    {
        if (profile.MaxOutput is int maximum)
        {
            if (maximum <= 0 || model.MaxOutput is not int supported || maximum > supported)
            {
                throw Invalid(
                    profile,
                    "maxOutput",
                    $"model '{model.Id}' does not support a positive limit up to {model.MaxOutput?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}"
                );
            }
        }
    }

    private static void ValidateTemperature(ModelProfile profile, ModelInfo model)
    {
        if (profile.Temperature is decimal temperature)
        {
            if (!model.SupportsTemperature || temperature is < 0m or > 2m)
            {
                throw Invalid(
                    profile,
                    "temperature",
                    $"model '{model.Id}' does not support this value"
                );
            }
        }
    }

    private static int ValidateContextWindow(ModelProfile profile, ModelInfo model)
    {
        if (profile.ContextWindow is not int window)
        {
            return model.ContextWindow;
        }

        if (window <= 0 || model.ContextWindow <= 0 || window > model.ContextWindow)
        {
            throw Invalid(
                profile,
                "contextWindow",
                $"must be positive and no greater than model '{model.Id}' window {model.ContextWindow.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            );
        }

        return window;
    }

    private static void ValidateDecisionThreshold(ModelProfile profile)
    {
        if (
            profile.DecisionThreshold is double threshold
            && (!double.IsFinite(threshold) || threshold is < 0 or > 1)
        )
        {
            throw Invalid(
                profile,
                "decisionThreshold",
                "must be a finite probability from 0 through 1"
            );
        }
    }

    private static InvalidDataException Invalid(
        ModelProfile profile,
        string field,
        string reason
    ) => new($"Profile '{profile.Name}' has invalid '{field}': {reason}.");
}
