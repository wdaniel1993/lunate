using System.Text.Json;

namespace Lunate.Agent;

internal static partial class SessionFormat
{
    private static void WriteModelUsage(Utf8JsonWriter writer, SessionModelUsageEntry entry)
    {
        writer.WriteStartObject();
        WriteEnvelope(writer, ModelUsageType, entry.Id, entry.ParentId, entry.Timestamp);
        if (entry.Model is { } model)
        {
            writer.WriteString("model", model);
        }

        if (entry.Usage is { } usage)
        {
            writer.WritePropertyName("usage");
            writer.WriteStartObject();
            writer.WriteNumber("input", usage.Input);
            writer.WriteNumber("output", usage.Output);
            if (usage.CacheRead is > 0 and int cacheRead)
            {
                writer.WriteNumber("cacheRead", cacheRead);
            }

            if (usage.CacheWrite is > 0 and int cacheWrite)
            {
                writer.WriteNumber("cacheWrite", cacheWrite);
            }

            writer.WriteEndObject();
        }

        if (entry.Attribution is { } attribution)
        {
            writer.WritePropertyName("attribution");
            writer.WriteStartObject();
            writer.WriteString("usage", attribution.Usage);
            writer.WriteString("profileName", attribution.ProfileName);
            writer.WriteString("providerName", attribution.ProviderName);
            writer.WriteString("providerKind", attribution.ProviderKind);
            if (attribution.DecisionMethod is { } method)
            {
                writer.WriteString("decisionMethod", method);
            }

            if (attribution.Probability is { } probability)
            {
                writer.WriteNumber("probability", probability);
            }

            writer.WriteEndObject();
        }

        if (entry.DecisionOption is { } decisionOption)
        {
            writer.WriteString("decisionOption", decisionOption);
        }

        writer.WriteEndObject();
    }
}
