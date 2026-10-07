using System.Globalization;
using System.Text.Json;

namespace Lunate.Extensibility.Abstractions;

public sealed record ExtensionManifest
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    public required string ApiVersion { get; init; }

    public required string EntryAssembly { get; init; }

    public IReadOnlyList<string> Tools { get; init; } = [];

    public IReadOnlyList<string> Commands { get; init; } = [];

    public IReadOnlyList<string> Hooks { get; init; } = [];

    public IReadOnlyList<string> Services { get; init; } = [];

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public IReadOnlyList<ModelProviderDescriptor> ModelProviders { get; init; } = [];

    public JsonElement? SettingsSchema { get; init; }

    public static ExtensionManifest Parse(string json, string fileName)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"{fileName}: manifest is not valid JSON: {exception.Message}",
                exception
            );
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{fileName}: manifest must be a JSON object.");
            }

            string id = RequiredString(root, fileName, "id");
            if (!IsValidId(id))
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'id' must match [a-z0-9-]+ but is '{id}'."
                );
            }

            string apiVersion = RequiredString(root, fileName, "apiVersion");
            try
            {
                _ = ExtensionApi.IsCompatible(apiVersion);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'apiVersion': {exception.Message}",
                    exception
                );
            }

            string entryAssembly = RequiredString(root, fileName, "entryAssembly");
            if (
                Path.IsPathRooted(entryAssembly)
                || entryAssembly.Contains('/')
                || entryAssembly.Contains('\\')
            )
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'entryAssembly' must be a file name inside the extension directory."
                );
            }

            return new ExtensionManifest
            {
                Id = id,
                Version = RequiredString(root, fileName, "version"),
                ApiVersion = apiVersion,
                EntryAssembly = entryAssembly,
                Tools = DeclarationArray(root, fileName, "tools"),
                Commands = DeclarationArray(root, fileName, "commands"),
                Hooks = DeclarationArray(root, fileName, "hooks"),
                Services = DeclarationArray(root, fileName, "services"),
                Capabilities = StringArray(root, fileName, "capabilities"),
                ModelProviders = ModelProviderArray(root, fileName),
                SettingsSchema = OptionalSettingsSchema(root, fileName),
            };
        }
    }

    private static bool IsValidId(string id) =>
        id.Length > 0
        && id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

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

            string endpoint = RequiredProviderString(item, fileName, "endpoint", $"{path}.endpoint");
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
            if (modelId.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(modelId.GetString()))
            {
                throw new InvalidDataException(
                    $"{fileName}: field '{name}' must be a non-empty array of strings."
                );
            }

            modelIds.Add(modelId.GetString()!);
        }

        return modelIds;
    }

    private static string RequiredString(JsonElement root, string fileName, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            throw new InvalidDataException($"{fileName}: field '{name}' is required.");
        }

        if (element.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(element.GetString()))
        {
            throw new InvalidDataException(
                $"{fileName}: field '{name}' must be a non-empty string."
            );
        }

        return element.GetString()!;
    }

    private static IReadOnlyList<string> DeclarationArray(
        JsonElement root,
        string fileName,
        string name
    )
    {
        List<string> values = StringArray(root, fileName, name);
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            string duplicate = values.First(value => values.Count(item => item == value) > 1);
            throw new InvalidDataException(
                $"{fileName}: field '{name}' declares '{duplicate}' more than once."
            );
        }

        return values;
    }

    private static List<string> StringArray(JsonElement root, string fileName, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"{fileName}: field '{name}' must be an array of strings."
            );
        }

        List<string> values = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    $"{fileName}: field '{name}' must be an array of strings."
                );
            }

            values.Add(item.GetString()!);
        }

        return values;
    }

    private static JsonElement? OptionalSettingsSchema(JsonElement root, string fileName)
    {
        if (!root.TryGetProperty("settingsSchema", out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"{fileName}: field 'settingsSchema' must be an object."
            );
        }

        return element.Clone();
    }
}
