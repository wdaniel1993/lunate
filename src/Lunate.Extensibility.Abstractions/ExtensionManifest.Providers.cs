using System.Globalization;
using System.Text.Json;

namespace Lunate.Extensibility.Abstractions;

public sealed partial record ExtensionManifest
{
    private static IReadOnlyList<ModelProviderDescriptor> ModelProviderArray(
        JsonElement root,
        string fileName
    )
    {
        if (!root.TryGetProperty("modelProviders", out JsonElement element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"{fileName}: field 'modelProviders' must be an array of objects."
            );
        }

        List<ModelProviderDescriptor> providers = [];
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'modelProviders' must be an array of objects."
                );
            }

            string path = $"modelProviders[{index.ToString(CultureInfo.InvariantCulture)}]";
            string id = RequiredProviderString(item, fileName, "id", $"{path}.id");
            if (!IsValidId(id))
            {
                throw new InvalidDataException(
                    $"{fileName}: field '{path}.id' must match [a-z0-9-]+ but is '{id}'."
                );
            }

            string endpoint = RequiredProviderString(
                item,
                fileName,
                "endpoint",
                $"{path}.endpoint"
            );
            if (!IsHttpEndpoint(endpoint))
            {
                throw new InvalidDataException(
                    $"{fileName}: field '{path}.endpoint' must be an absolute http(s) URL but is '{endpoint}'."
                );
            }

            providers.Add(
                new ModelProviderDescriptor(
                    id,
                    RequiredProviderString(item, fileName, "displayName", $"{path}.displayName"),
                    endpoint,
                    RequiredProviderString(item, fileName, "secretName", $"{path}.secretName"),
                    RequiredModelIds(item, fileName, $"{path}.modelIds")
                )
            );
            index++;
        }

        IEnumerable<string> duplicates = providers
            .GroupBy(provider => provider.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        if (duplicates.FirstOrDefault() is { } duplicate)
        {
            throw new InvalidDataException(
                $"{fileName}: field 'modelProviders' declares id '{duplicate}' more than once."
            );
        }

        return providers;
    }

    private static bool IsHttpEndpoint(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string RequiredProviderString(
        JsonElement item,
        string fileName,
        string property,
        string path
    )
    {
        if (!item.TryGetProperty(property, out JsonElement element))
        {
            throw new InvalidDataException($"{fileName}: field '{path}' is required.");
        }

        if (element.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(element.GetString()))
        {
            throw new InvalidDataException(
                $"{fileName}: field '{path}' must be a non-empty string."
            );
        }

        return element.GetString()!;
    }

    private static IReadOnlyList<string> RequiredModelIds(
        JsonElement item,
        string fileName,
        string name
    )
    {
        if (!item.TryGetProperty("modelIds", out JsonElement element))
        {
            throw new InvalidDataException($"{fileName}: field '{name}' is required.");
        }

        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
        {
            throw new InvalidDataException(
                $"{fileName}: field '{name}' must be a non-empty array of strings."
            );
        }

        List<string> modelIds = [];
        foreach (JsonElement modelId in element.EnumerateArray())
        {
            if (
                modelId.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(modelId.GetString())
            )
            {
                throw new InvalidDataException(
                    $"{fileName}: field '{name}' must be a non-empty array of strings."
                );
            }

            modelIds.Add(modelId.GetString()!);
        }

        return modelIds;
    }
}
