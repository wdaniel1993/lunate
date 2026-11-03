using System.Text.Json;

namespace Lunate.Ai;

/// <summary>
/// Parses a declared credential placement from a settings or preset file: a compact string
/// (<c>"bearer"</c>, <c>"x-api-key"</c>, <c>"query:key"</c>) or an object
/// (<c>{ "in": "header", "name": "x-api-key" }</c>) applies to every family the provider serves,
/// and an object keyed by family name applies one per family an entry
/// (for example <c>{ "google": { "in": "query", "name": "key" } }</c> for a provider whose two
/// surfaces want different credentials — the OpenAI-compatible one keeps the bearer its endpoint
/// documents while the native one takes the query parameter).
/// </summary>
internal static class CredentialPlacementFile
{
    internal static bool TryParseProvider(
        JsonElement? declared,
        out Dictionary<ProviderApiFamily, CredentialPlacement>? placements,
        out string? error
    )
    {
        placements = null;
        error = null;
        if (!declared.HasValue)
        {
            return true;
        }

        JsonElement element = declared.Value;
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return true;
            case JsonValueKind.String:
                bool compactParsed = ToAllFamilies(element, out placements, out error);
                return compactParsed;
            case JsonValueKind.Object:
                return ParseObject(element, out placements, out error);
            default:
                error = "the placement must be a string or an object";
                return false;
        }
    }

    private static bool ParseObject(
        JsonElement element,
        out Dictionary<ProviderApiFamily, CredentialPlacement>? placements,
        out string? error
    )
    {
        placements = null;
        error = null;
        if (element.TryGetProperty("in", out JsonElement target))
        {
            CredentialPlacement? single;
            if (!FromObject(element, target, out single, out error))
            {
                return false;
            }

            placements = ToEveryFamily(single!);
            return true;
        }

        placements = [];
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!ProviderApiFamilies.TryParse(property.Name, out ProviderApiFamily family))
            {
                error =
                    $"family '{property.Name}' is not an API family; supported families: {ProviderApiFamilies.Supported()}";
                return false;
            }

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    if (
                        !CredentialPlacement.TryParseCompact(
                            property.Value.GetString(),
                            out CredentialPlacement? compact,
                            out error
                        )
                    )
                    {
                        return false;
                    }

                    placements[family] = compact!;
                    break;
                case JsonValueKind.Object:
                    if (!property.Value.TryGetProperty("in", out JsonElement keyedTarget))
                    {
                        error = "the placement per family is missing an 'in'";
                        return false;
                    }

                    if (
                        !FromObject(
                            property.Value,
                            keyedTarget,
                            out CredentialPlacement? keyed,
                            out error
                        )
                    )
                    {
                        return false;
                    }

                    placements[family] = keyed!;
                    break;
                default:
                    error = "the placement per family must be a string or an object";
                    return false;
            }
        }

        return true;
    }

    private static bool ToAllFamilies(
        JsonElement stringElement,
        out Dictionary<ProviderApiFamily, CredentialPlacement>? placements,
        out string? error
    )
    {
        if (
            !CredentialPlacement.TryParseCompact(
                stringElement.GetString(),
                out CredentialPlacement? single,
                out error
            )
        )
        {
            placements = null;
            return false;
        }

        placements = ToEveryFamily(single!);
        return true;
    }

    private static Dictionary<ProviderApiFamily, CredentialPlacement> ToEveryFamily(
        CredentialPlacement placement
    ) => ProviderApiFamilies.All.ToDictionary(family => family, _ => placement);

    /// <summary>One placement object: <c>in</c>, <c>name</c> and an optional <c>scheme</c>.</summary>
    private static bool FromObject(
        JsonElement element,
        JsonElement target,
        out CredentialPlacement? placement,
        out string? error
    )
    {
        placement = null;
        error = null;
        if (target.ValueKind != JsonValueKind.String)
        {
            error = "the placement's 'in' must be 'header' or 'query'";
            return false;
        }

        CredentialPlacementTarget parsedTarget;
        switch (target.GetString())
        {
            case "header":
                parsedTarget = CredentialPlacementTarget.Header;
                break;
            case "query":
                parsedTarget = CredentialPlacementTarget.Query;
                break;
            default:
                error = "the placement's 'in' must be 'header' or 'query'";
                return false;
        }

        string? name = ObjectString(element, "name");
        if (name is null)
        {
            error = "the placement is missing a 'name'";
            return false;
        }

        return CredentialPlacement.TryCreate(
            name,
            parsedTarget,
            ObjectString(element, "scheme"),
            out placement,
            out error
        );
    }

    private static string? ObjectString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement part)
        && part.ValueKind == JsonValueKind.String
            ? part.GetString()
            : null;
}
