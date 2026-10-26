using System.Text.Json;

namespace Lunate.Extensibility.Restore;

/// <summary>One content kind a package declares, and the package-relative folder it lives in.</summary>
internal sealed record PackageContent(string Kind, string Directory);

/// <summary>
/// A package's <c>lunate.package.json</c> (design D2): schema 1, one folder per content kind, in the
/// order the manifest declares them. Unknown fields are ignored so a package written for a later
/// Lunate still parses; a known field of the wrong type, a kind this version does not install, and a
/// manifest that declares nothing are refusals that name what a package author has to change.
/// </summary>
internal sealed record PackageManifest
{
    public const int CurrentSchema = 1;
    public const string ExtensionKind = "extension";
    public const string SkillsKind = "skills";

    public required int Schema { get; init; }

    /// <summary>The declared folders, in the order the manifest declares them.</summary>
    public required IReadOnlyList<PackageContent> Content { get; init; }

    /// <summary>The <c>extension</c> folder, or null when the manifest does not declare one.</summary>
    public string? Extension => DirectoryOf(ExtensionKind);

    /// <summary>The <c>skills</c> folder, or null when the manifest does not declare one.</summary>
    public string? Skills => DirectoryOf(SkillsKind);

    public static PackageManifest Parse(string json, string fileName)
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

            int schema = ReadSchema(root, fileName);
            List<PackageContent> content = [];
            HashSet<string> declared = new(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "schema":
                        break;
                    case ExtensionKind:
                    case SkillsKind:
                        if (!declared.Add(property.Name))
                        {
                            throw new InvalidDataException(
                                $"{fileName}: field '{property.Name}' is declared more than once; "
                                    + "declare each content kind once."
                            );
                        }

                        content.Add(
                            new PackageContent(
                                property.Name,
                                PackagePaths.Folder(property.Value, fileName, property.Name)
                            )
                        );
                        break;
                    case "templates":
                    case "themes":
                        // Refused rather than ignored: a user who declares a kind must not believe
                        // content landed that this version never wrote (design D2, spec: layout).
                        throw new InvalidDataException(
                            $"{fileName} declares the '{property.Name}' kind, which this version does "
                                + $"not install; remove '{property.Name}' from the package manifest or "
                                + "use a Lunate version that installs it."
                        );
                    default:
                        // Unknown fields are ignored: a package written for a later Lunate parses.
                        break;
                }
            }

            if (content.Count == 0)
            {
                throw new InvalidDataException(
                    $"{fileName} declares no content kind; declare '{ExtensionKind}', "
                        + $"'{SkillsKind}' or both — a package that installs nothing is a mistake."
                );
            }

            return new PackageManifest { Schema = schema, Content = content };
        }
    }

    private string? DirectoryOf(string kind) =>
        Content
            .FirstOrDefault(item => string.Equals(item.Kind, kind, StringComparison.Ordinal))
            ?.Directory;

    private static int ReadSchema(JsonElement root, string fileName)
    {
        if (!root.TryGetProperty("schema", out JsonElement element))
        {
            throw new InvalidDataException(
                $"{fileName}: field 'schema' is required; it must be the integer {CurrentSchema}."
            );
        }

        if (
            element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out int schema)
            || schema != CurrentSchema
        )
        {
            throw new InvalidDataException(
                $"{fileName}: field 'schema' must be the integer {CurrentSchema}, the only manifest "
                    + "schema this version reads; this package declares "
                    + $"{element.GetRawText()}."
            );
        }

        return schema;
    }
}
