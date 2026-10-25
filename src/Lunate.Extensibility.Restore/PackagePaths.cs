using System.Text.Json;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// Path confinement (design D11): every folder a package manifest names and every entry an archive
/// yields has to resolve inside the package root. A rooted path, a <c>..</c> segment and an entry
/// that walks out of the root are one refusal — the entry is named, and the reason says why.
/// </summary>
internal static class PackagePaths
{
    /// <summary>
    /// The folder a manifest field names, as a package-relative path with forward slashes. A
    /// non-string is refused naming the field; a path that leaves the root is refused naming the
    /// path.
    /// </summary>
    internal static string Folder(JsonElement property, string fileName, string field)
    {
        if (
            property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString())
        )
        {
            throw new InvalidDataException(
                $"{fileName}: field '{field}' must be a non-empty string naming a folder in the "
                    + "package (for example \"extension\"); it is "
                    + $"{(property.ValueKind == JsonValueKind.String ? "empty" : property.ValueKind.ToString().ToLowerInvariant())}."
            );
        }

        string path = property.GetString()!;
        string? reason = Reason(path, out string normalised);
        if (reason is null)
        {
            return normalised;
        }

        throw new InvalidDataException(
            $"{fileName}: field '{field}' names '{path}', which resolves outside the package root "
                + $"({reason}); a package folder must stay inside it."
        );
    }

    /// <summary>One archive entry name, normalised; refused when it leaves the package root.</summary>
    internal static string Entry(string path)
    {
        string? reason = Reason(path, out string normalised);
        if (reason is null)
        {
            return normalised;
        }

        throw new InvalidDataException(
            $"archive entry '{path}' resolves outside the package root ({reason}); a package's "
                + "files must stay inside it."
        );
    }

    /// <summary>
    /// Null when the path stays inside the package root, otherwise the reason; the normalised
    /// package-relative path has forward slashes and no <c>.</c> or empty segments.
    /// </summary>
    private static string? Reason(string path, out string normalised)
    {
        normalised = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return "it is empty";
        }

        // Both separators are path separators to a zip reader on one platform or the other, so both
        // are treated as one before anything is judged.
        string logical = path.Replace('\\', '/');
        if (logical.StartsWith('/') || HasDriveLetter(logical) || Path.IsPathRooted(path))
        {
            return "it is an absolute path";
        }

        List<string> segments = [];
        foreach (string segment in logical.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                // Refused even when it would resolve back inside: a package path never needs one,
                // and judging "resolves outside" case by case is how a traversal slips through.
                return "it has a '..' segment";
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            return "it names the package root itself, not a folder or file in it";
        }

        normalised = string.Join('/', segments);
        return null;
    }

    private static bool HasDriveLetter(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    /// <summary>
    /// The folders NuGet writes itself: package metadata a reader never installs and never calls an
    /// undeclared content folder.
    /// </summary>
    internal static bool IsNuGetMetadata(string entry)
    {
        if (
            entry.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase)
            || entry.Equals(".signature.p7s", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        if (!entry.Contains('/'))
        {
            return entry.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase);
        }

        return entry.StartsWith("_rels/", StringComparison.Ordinal)
            || entry.StartsWith("package/", StringComparison.Ordinal);
    }
}
