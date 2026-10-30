using System.Text.Json;

namespace Lunate.Agent;

internal static partial class SessionFormat
{
    private static SessionModelUsageEntry ParseModelUsage(JsonElement root)
    {
        SessionUsage? usage =
            root.TryGetProperty("usage", out JsonElement usageElement)
            && usageElement.ValueKind == JsonValueKind.Object
                ? usageElement.Deserialize<SessionUsage>(SessionJson.Options)
                : null;
        ModelUsageAttribution? attribution = root.TryGetProperty(
            "attribution",
            out JsonElement attributionElement
        )
            ? ParseUsageAttribution(attributionElement)
            : null;
        string? decisionOption = OptionalString(root, "decisionOption");
        if (decisionOption is not null and not ("approve" or "deny"))
        {
            throw new InvalidDataException("Session model usage decision option is invalid.");
        }

        return new SessionModelUsageEntry(
            RequiredString(root, "id"),
            OptionalString(root, "parentId"),
            RequiredTimestamp(root, "timestamp"),
            OptionalString(root, "model"),
            usage,
            attribution,
            decisionOption
        );
    }
}
