using Lunate.Ai;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    /// <summary>Checks only known capability facts required by the active usage.</summary>
    private void EnsureUsageCapabilities()
    {
        ModelInfo? model = DeclaredModel();
        ModelCapabilityDescriptor? capabilities = model?.Capabilities;
        if (model is null || capabilities is null)
        {
            return;
        }

        if (_options.Usage == "chat")
        {
            RequireCapability(model, capabilities.InputModalities, "text", "input modality");
            RequireCapability(model, capabilities.Features, "tools", "feature");
        }

        if (
            _options.Usage == "decision"
            && capabilities.Features is { } decisionFeatures
            && !decisionFeatures.Contains("structured-output")
        )
        {
            throw new InvalidOperationException(
                $"model {model.Id} doesn't support structured-output required by decision usage."
            );
        }
    }

    private static void RequireCapability(
        ModelInfo model,
        IReadOnlySet<string>? known,
        string required,
        string category
    )
    {
        if (known is not null && !known.Contains(required))
        {
            throw new InvalidOperationException(
                $"model {model.Id} doesn't support required {category} '{required}'."
            );
        }
    }
}
