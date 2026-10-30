using Lunate.Ai;
using Microsoft.Extensions.AI;

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

    private static bool HasImageContent(IEnumerable<ChatMessage> messages) =>
        messages
            .SelectMany(static message => message.Contents)
            .OfType<DataContent>()
            .Any(static content =>
                content.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            );

    private static bool HasUnknownImageSupport(ModelInfo? model) =>
        model is not null && !model.SupportsVision && model.Capabilities?.InputModalities is null;

    private static bool IsImageInputRejection(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string message = current.Message;
            bool mediaTypeRejection = message.Contains("415", StringComparison.Ordinal);
            bool imageRejection =
                (
                    message.Contains("image", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("vision", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("attachment", StringComparison.OrdinalIgnoreCase)
                )
                && (
                    message.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("not support", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("doesn't accept", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("cannot accept", StringComparison.OrdinalIgnoreCase)
                );
            if (mediaTypeRejection || imageRejection)
            {
                return true;
            }
        }

        return false;
    }
}
