using System.Text.Json;

namespace Lunate.Extensibility.Abstractions;

public sealed partial record ExtensionManifest
{
    private static IReadOnlyList<WriteVerifierDeclaration> WriteVerifierArray(
        JsonElement root,
        string fileName
    )
    {
        if (!root.TryGetProperty("writeVerifiers", out JsonElement element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw InvalidWriteVerifiers(fileName, "must be an array of declarations");
        }

        List<WriteVerifierDeclaration> declarations = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw InvalidWriteVerifiers(fileName, "entries must be objects");
            }

            string name = RequiredVerifierName(item, fileName);
            if (!names.Add(name))
            {
                throw InvalidWriteVerifiers(fileName, $"declares verifier '{name}' more than once");
            }

            declarations.Add(new WriteVerifierDeclaration(name, RequiredPatterns(item, fileName)));
        }

        return declarations;
    }

    private static string RequiredVerifierName(JsonElement item, string fileName)
    {
        if (
            !item.TryGetProperty("name", out JsonElement name)
            || name.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(name.GetString())
        )
        {
            throw InvalidWriteVerifiers(fileName, "each entry needs a non-empty 'name'");
        }

        return name.GetString()!;
    }

    private static IReadOnlyList<string> RequiredPatterns(JsonElement item, string fileName)
    {
        if (
            !item.TryGetProperty("patterns", out JsonElement patterns)
            || patterns.ValueKind != JsonValueKind.Array
            || patterns.GetArrayLength() == 0
        )
        {
            throw InvalidWriteVerifiers(fileName, "each entry needs a non-empty 'patterns' array");
        }

        List<string> values = [];
        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement pattern in patterns.EnumerateArray())
        {
            if (pattern.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(pattern.GetString()))
            {
                throw InvalidWriteVerifiers(fileName, "patterns must be non-empty filename globs");
            }

            string value = pattern.GetString()!;
            if (value.Contains('/') || value.Contains('\\'))
            {
                throw InvalidWriteVerifiers(fileName, $"pattern '{value}' must not contain a path separator");
            }

            if (!unique.Add(value))
            {
                throw InvalidWriteVerifiers(fileName, $"pattern '{value}' is duplicated");
            }

            values.Add(value);
        }

        return values;
    }

    private static InvalidDataException InvalidWriteVerifiers(string fileName, string reason) =>
        new($"{fileName}: field 'writeVerifiers' {reason}.");
}
