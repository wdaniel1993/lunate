using System.Collections.Frozen;
using System.Text.Json;

namespace Lunate.Extensibility.Abstractions;

public sealed partial record ExtensionManifest
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    public required string ApiVersion { get; init; }

    /// <summary>
    /// The in-process entry assembly file name; null for a remote extension (exactly one of
    /// <see cref="EntryAssembly"/> and <see cref="Runtime"/> is set).
    /// </summary>
    public string? EntryAssembly { get; init; }

    /// <summary>
    /// The out-of-process runtime; null for an in-process extension (exactly one of
    /// <see cref="EntryAssembly"/> and <see cref="Runtime"/> is set).
    /// </summary>
    public ExtensionRuntime? Runtime { get; init; }

    public IReadOnlyList<string> Tools { get; init; } = [];

    public IReadOnlyList<string> Commands { get; init; } = [];

    public IReadOnlyList<string> Hooks { get; init; } = [];

    public IReadOnlyList<string> Services { get; init; } = [];

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public IReadOnlyList<ModelProviderDescriptor> ModelProviders { get; init; } = [];

    /// <summary>The declared write verifiers and the basename patterns each one handles.</summary>
    public IReadOnlyList<WriteVerifierDeclaration> WriteVerifiers { get; init; } = [];

    /// <summary>
    /// True when the manifest asserts built-in provenance: the extension ships with the host and its
    /// code runs in the default <c>AssemblyLoadContext</c>. Defaults to <see langword="false"/>;
    /// a normal manifest omits the field. Who may set it is enforced at load (see the built-in
    /// provenance requirement), not here.
    /// </summary>
    public bool BuiltIn { get; init; }

    /// <summary>The per-workspace services the extension declares, in declaration order.</summary>
    public IReadOnlyList<ExtensionWorkspaceService> WorkspaceServices { get; init; } = [];

    /// <summary>
    /// The activation rules by tool local name, plus the default rule under the <c>*</c> key; a
    /// declared tool absent from the dictionary is always available.
    /// </summary>
    public IReadOnlyDictionary<string, ExtensionToolActivationRule> ToolActivation { get; init; } =
        FrozenDictionary<string, ExtensionToolActivationRule>.Empty;

    /// <summary>The model-facing aliases by tool local name; a tool absent here keeps its canonical
    /// <c>&lt;id&gt;__&lt;local&gt;</c> name.</summary>
    public IReadOnlyDictionary<string, string> ToolAliases { get; init; } =
        FrozenDictionary<string, string>.Empty;

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
            if (id.Contains(':'))
            {
                // A command's host-side id is ext:<id>:<name>, so a colon in the id would make it
                // ambiguous and unparseable in every diagnostic (design D2, sign-off Q4).
                throw new InvalidDataException(
                    $"{fileName}: field 'id' must not contain ':' because command ids read 'ext:<id>:<name>'; rename the extension."
                );
            }

            if (!ExtensionIds.IsValid(id))
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'id' must match {ExtensionIds.Pattern} but is '{id}'."
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

            string? entryAssembly = OptionalName(root, fileName, "entryAssembly");
            ExtensionRuntime? runtime = ExtensionRuntime.Parse(root, fileName);
            // A built-in's code is the host's own assemblies, so the exactly-one rule between
            // 'entryAssembly' and 'runtime' applies to non-built-in manifests only (design §1);
            // the provenance checks that keep 'builtin' in its lane are the loader's decisions,
            // made where the manifest's source is known.
            if (!OptionalBuiltIn(root, fileName) && (entryAssembly is null) == (runtime is null))
            {
                throw new InvalidDataException(
                    entryAssembly is null
                        ? $"{fileName}: the manifest declares neither 'entryAssembly' nor 'runtime'; declare exactly one runtime kind."
                        : $"{fileName}: the manifest declares both 'entryAssembly' and 'runtime'; declare exactly one runtime kind."
                );
            }

            IReadOnlyList<string> tools = DeclarationArray(root, fileName, "tools");

            return new ExtensionManifest
            {
                Id = id,
                Version = RequiredString(root, fileName, "version"),
                ApiVersion = apiVersion,
                EntryAssembly = entryAssembly,
                Runtime = runtime,
                BuiltIn = OptionalBuiltIn(root, fileName),
                Tools = tools,
                WorkspaceServices = WorkspaceServiceArray(root, fileName),
                ToolActivation = ToolActivationObject(root, fileName, tools),
                ToolAliases = ToolAliasesObject(root, fileName, tools),
                Commands = DeclarationArray(root, fileName, "commands"),
                Hooks = DeclarationArray(root, fileName, "hooks"),
                Services = DeclarationArray(root, fileName, "services"),
                Capabilities = StringArray(root, fileName, "capabilities"),
                ModelProviders = ModelProviderArray(root, fileName),
                WriteVerifiers = WriteVerifierArray(root, fileName),
                SettingsSchema = OptionalSettingsSchema(root, fileName),
            };
        }
    }

    private static string? OptionalName(JsonElement root, string fileName, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(element.GetString()))
        {
            throw new InvalidDataException(
                $"{fileName}: field '{name}' must be a non-empty string."
            );
        }

        string value = element.GetString()!;
        if (Path.IsPathRooted(value) || value.Contains('/') || value.Contains('\\'))
        {
            throw new InvalidDataException(
                $"{fileName}: field '{name}' must be a file name inside the extension directory."
            );
        }

        return value;
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
