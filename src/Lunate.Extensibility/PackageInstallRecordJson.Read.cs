using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunate.Extensibility;

/// <summary>
/// Reading <c>install.json</c>: the shape, the two halves, the scope and worktree rules, path
/// confinement, and the integrity hash that decides whether any of it can be believed. One
/// refusal shape — null and a one-line reason naming the file — so a caller reports the record
/// instead of acting on it.
/// </summary>
public static partial class PackageInstallRecordJson
{
    private const string BrokeMessage =
        "nothing acts on it — reinstall the package to write a new record";

    /// <summary>
    /// Parses record text. Anything this version cannot vouch for yields null and a one-line reason
    /// naming <paramref name="label"/>: a shape it does not read, a path that leaves the scope root,
    /// or an integrity hash that does not answer for the bytes. <paramref name="acceptModified"/>
    /// keeps the record when only its integrity hash disagrees, and still reports the reason — what
    /// <c>--force</c> needs, and nothing else uses.
    /// </summary>
    public static PackageInstallRecord? TryParse(string json, string label, out string? problem) =>
        TryParse(json, label, acceptModified: false, out problem);

    private static PackageInstallRecord? TryParse(
        string json,
        string label,
        bool acceptModified,
        out string? problem
    )
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            problem = $"{label} is not valid JSON: {exception.Message}";
            return null;
        }

        if (node is not JsonObject document)
        {
            problem = $"{label} is not a JSON object";
            return null;
        }

        if (
            document["recordVersion"] is not JsonValue versionValue
            || !versionValue.TryGetValue(out int version)
        )
        {
            problem = $"{label} has no usable recordVersion";
            return null;
        }

        if (version != PackageInstallRecord.CurrentVersion)
        {
            problem = FormattableString.Invariant(
                $"{label} has recordVersion {version}, but this Lunate writes {PackageInstallRecord.CurrentVersion}"
            );
            return null;
        }

        if (document["package"] is not JsonObject package)
        {
            problem = $"{label} has no package block";
            return null;
        }

        string? id = Text(package, "id");
        string? packageVersion = Text(package, "version");
        string? sha512 = Text(package, "sha512");
        if (id is null || packageVersion is null || sha512 is null)
        {
            problem = $"{label} is missing the package id, version or sha512";
            return null;
        }

        if (document["content"] is not JsonObject content)
        {
            problem = $"{label} has no content block";
            return null;
        }

        if (
            Scope(document["scope"]) is not { } scope
            || Text(document, "installedAt") is not { Length: > 0 } installedAt
        )
        {
            problem = $"{label} is missing the scope ('user' or 'project') or the install time";
            return null;
        }

        string? worktree = Text(document, "worktree");
        if (scope == PackageScope.Project && worktree is not { Length: > 0 })
        {
            problem = $"{label} records a project install without the worktree it belongs to";
            return null;
        }

        if (
            Paths(content["paths"], label, out List<InstalledFile> paths, out string? pathsProblem)
            is false
        )
        {
            problem = pathsProblem;
            return null;
        }

        if (Text(document, "integrity") is not { Length: 64 } integrity)
        {
            problem = $"{label} has no integrity hash";
            return null;
        }

        PackageInstallRecord record = new()
        {
            Package = new InstalledPackage
            {
                Id = id,
                Version = packageVersion,
                Sha512 = sha512,
                Sources = Strings(package["sources"]),
                Files = Strings(package["files"]),
                ExtensionId = Text(package, "extension"),
            },
            Content = new InstalledContent
            {
                ExtensionId = content["extension"] is JsonObject extension
                    ? Text(extension, "id")
                    : null,
                ExtensionVersion = content["extension"] is JsonObject installed
                    ? Text(installed, "version")
                    : null,
                Skills = Strings(content["skills"]),
                Paths = paths,
            },
            Scope = scope,
            Worktree = worktree,
            InstalledAt = installedAt,
        };

        // The record answers for itself: a field edited without recomputing the hash is refused,
        // never used to uninstall or trust anything.
        if (!string.Equals(integrity, Integrity(Content(record)), StringComparison.Ordinal))
        {
            problem =
                $"{label} was modified after it was written (its integrity hash does not match); "
                + BrokeMessage;
            return acceptModified ? record : null;
        }

        problem = null;
        return record;
    }

    /// <summary>The recorded paths, each confined to the scope root.</summary>
    private static bool Paths(
        JsonNode? node,
        string label,
        out List<InstalledFile> paths,
        out string? problem
    )
    {
        paths = [];
        problem = null;
        if (node is not JsonArray entries)
        {
            problem = $"{label} lists no content paths";
            return false;
        }

        foreach (JsonNode? entry in entries)
        {
            if (entry is not JsonObject file)
            {
                problem = $"{label} holds a content path that is not an object";
                return false;
            }

            string? recorded = Text(file, "path");
            if (
                recorded is null
                || file["size"] is not JsonValue sizeValue
                || !sizeValue.TryGetValue(out long size)
                || size < 0
                || Text(file, "sha256") is not { Length: > 0 } sha256
            )
            {
                problem = FormattableString.Invariant(
                    $"{label} holds the content path '{recorded}' without a usable size or sha256"
                );
                return false;
            }

            if (TryConfine(recorded, out string normalised, out string? reason) is false)
            {
                problem =
                    $"{label} names the path '{recorded}', which does not stay inside the scope "
                    + $"root ({reason}); every recorded path is relative to the scope root";
                return false;
            }

            paths.Add(new InstalledFile(normalised, size, sha256));
        }

        return true;
    }

    /// <summary>
    /// A recorded path stays inside the scope root: no rooted path, no drive letter, no <c>..</c>
    /// segment. The normalised form has forward slashes on every platform.
    /// </summary>
    internal static bool TryConfine(string path, out string normalised, out string? reason)
    {
        normalised = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "it is empty";
            return false;
        }

        string logical = path.Replace('\\', '/');
        if (
            logical.StartsWith('/')
            || (logical.Length >= 2 && char.IsAsciiLetter(logical[0]) && logical[1] == ':')
            || Path.IsPathRooted(path)
        )
        {
            reason = "it is an absolute path";
            return false;
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
                reason = "it has a '..' segment";
                return false;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            reason = "it names no file";
            return false;
        }

        normalised = string.Join('/', segments);
        reason = null;
        return true;
    }

    private static PackageScope? Scope(JsonNode? node) =>
        Text(node) switch
        {
            "user" => PackageScope.User,
            "project" => PackageScope.Project,
            _ => null,
        };

    private static List<string> Strings(JsonNode? node)
    {
        List<string> values = [];
        if (node is JsonArray array)
        {
            foreach (JsonNode? entry in array)
            {
                if (entry is JsonValue value && value.TryGetValue<string>(out string? text))
                {
                    values.Add(text);
                }
            }
        }

        return values;
    }
}
