using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Coding;

public static partial class SettingsStore
{
    private static ModelCapabilityDescriptor? ReadProfileCapabilities(
        JsonElement value,
        string profile,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"'profiles.{profile}.capabilities' must be an object");
            return null;
        }

        IReadOnlySet<string>? apis = null;
        IReadOnlySet<string>? inputModalities = null;
        IReadOnlySet<string>? outputModalities = null;
        IReadOnlySet<string>? features = null;
        foreach (JsonProperty field in value.EnumerateObject())
        {
            switch (field.Name)
            {
                case "apis":
                    apis = ReadCapabilitySet(field.Value, profile, field.Name, problems);
                    break;
                case "inputModalities":
                    inputModalities = ReadCapabilitySet(field.Value, profile, field.Name, problems);
                    break;
                case "outputModalities":
                    outputModalities = ReadCapabilitySet(
                        field.Value,
                        profile,
                        field.Name,
                        problems
                    );
                    break;
                case "features":
                    features = ReadCapabilitySet(field.Value, profile, field.Name, problems);
                    break;
                default:
                    problems.Add($"unknown key 'profiles.{profile}.capabilities.{field.Name}'");
                    break;
            }
        }

        return new ModelCapabilityDescriptor(apis, inputModalities, outputModalities, features);
    }

    private static IReadOnlySet<string>? ReadCapabilitySet(
        JsonElement value,
        string profile,
        string category,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            problems.Add(
                $"'profiles.{profile}.capabilities.{category}' must be an array of non-empty strings"
            );
            return null;
        }

        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())
            )
            {
                problems.Add(
                    $"'profiles.{profile}.capabilities.{category}' must contain non-empty strings"
                );
                return null;
            }

            values.Add(item.GetString()!);
        }

        return values;
    }
}
