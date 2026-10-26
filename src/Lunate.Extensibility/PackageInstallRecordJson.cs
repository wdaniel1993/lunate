using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunate.Extensibility;

/// <summary>
/// Reads, writes and hashes <c>install.json</c> (design D8, D9). The writer is canonical — sources,
/// archive files, skill names and content paths are ordered here, and the field order is fixed — so
/// the same record always serializes byte-identically and re-reading then re-writing one produces
/// the same bytes. The <c>integrity</c> field is the SHA-256, hex lower, of the document with that
/// field empty, so a hand edit reads <c>broken</c> instead of being believed. A write goes to a
/// temporary file in the same directory and is moved into place, so a reader never sees half a
/// record.
/// </summary>
public static partial class PackageInstallRecordJson
{
    /// <summary>The record's file name inside <c>&lt;root&gt;/packages/&lt;package-id&gt;/</c>.</summary>
    public const string FileName = "install.json";

    // NewLine is pinned: the default follows Environment.NewLine, which would make the same record
    // two different byte sequences on Windows and POSIX — and the integrity hash covers those bytes.
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    /// <summary>The canonical JSON text of <paramref name="record"/>, with a trailing newline.</summary>
    public static string Write(PackageInstallRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Document(record).ToJsonString(Indented) + "\n";
    }

    /// <summary>The record's canonical bytes, UTF-8 without a byte-order mark.</summary>
    public static byte[] ToBytes(PackageInstallRecord record) => Utf8(Write(record));

    /// <summary>
    /// Writes the canonical text through a temporary file in the same directory, then moves it into
    /// place: the record is written last and atomically, so it never names content that is absent.
    /// </summary>
    public static void WriteTo(string path, PackageInstallRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(record);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, ToBytes(record));
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // A half-written record is nothing; the temporary copy goes with the failure.
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    /// <summary>
    /// Reads a record file; null without a problem when it is absent. An unreadable, unparsable or
    /// edited record yields null and the reason, naming the file — the caller reports it and acts on
    /// nothing. The read shares the file, so it never races the writer or a replace.
    /// </summary>
    public static PackageInstallRecord? TryRead(string path, out string? problem) =>
        TryRead(path, acceptModified: false, out problem);

    /// <summary>
    /// Reads a record for <c>--force</c>: an integrity hash that no longer answers for the bytes is
    /// reported as the problem instead of withholding the record, because a forced uninstall may act
    /// on content whose paths are all confined (spec: "The install record"). Every other problem
    /// still yields null: a record this version cannot read is one nothing acts on.
    /// </summary>
    public static PackageInstallRecord? TryReadForForce(string path, out string? problem) =>
        TryRead(path, acceptModified: true, out problem);

    private static PackageInstallRecord? TryRead(
        string path,
        bool acceptModified,
        out string? problem
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        problem = null;
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true
            );
            text = reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = $"the record file '{path}' could not be read: {exception.Message}";
            return null;
        }

        return TryParse(text, $"the record file '{path}'", acceptModified, out problem);
    }

    /// <summary>The two halves, the scope, the worktree and the install time, with the integrity hash last.</summary>
    private static JsonObject Document(PackageInstallRecord record)
    {
        JsonObject document = Content(record);
        document["integrity"] = Integrity(document);
        return document;
    }

    /// <summary>The hash covers every field above <c>integrity</c>, which is written empty for the hash.</summary>
    private static string Integrity(JsonObject document)
    {
        JsonObject copy = document.DeepClone().AsObject();
        copy["integrity"] = string.Empty;
        return Convert.ToHexStringLower(SHA256.HashData(Utf8(copy.ToJsonString(Indented) + "\n")));
    }

    private static JsonObject Content(PackageInstallRecord record)
    {
        var package = new JsonObject
        {
            ["id"] = record.Package.Id,
            ["version"] = record.Package.Version,
            ["sha512"] = record.Package.Sha512,
            ["sources"] = Strings(record.Package.Sources),
            ["files"] = Strings(record.Package.Files),
        };
        if (record.Package.ExtensionId is { Length: > 0 } lockOwner)
        {
            package["extension"] = lockOwner;
        }

        JsonObject content = new();
        if (record.Content.ExtensionId is { Length: > 0 } extensionId)
        {
            content["extension"] = new JsonObject
            {
                ["id"] = extensionId,
                ["version"] = record.Content.ExtensionVersion ?? string.Empty,
            };
        }

        content["skills"] = Strings(record.Content.Skills);
        var paths = new JsonArray();
        foreach (
            InstalledFile file in record.Content.Paths.OrderBy(
                entry => entry.Path,
                StringComparer.Ordinal
            )
        )
        {
            paths.Add(
                new JsonObject
                {
                    ["path"] = file.Path,
                    ["size"] = file.Size,
                    ["sha256"] = file.Sha256,
                }
            );
        }

        content["paths"] = paths;

        JsonObject document = new()
        {
            ["recordVersion"] = PackageInstallRecord.CurrentVersion,
            ["package"] = package,
            ["content"] = content,
            ["scope"] = ScopeText(record.Scope),
            ["installedAt"] = record.InstalledAt,
        };
        if (record.Worktree is { Length: > 0 } worktree)
        {
            document["worktree"] = worktree;
        }

        return document;
    }

    internal static string ScopeText(PackageScope scope) =>
        scope == PackageScope.Project ? "project" : "user";

    private static JsonArray Strings(IReadOnlyList<string>? values)
    {
        var array = new JsonArray();
        if (values is null)
        {
            return array;
        }

        foreach (
            string value in values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
        )
        {
            array.Add(value);
        }

        return array;
    }

    private static byte[] Utf8(string text) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    private static string? Text(JsonObject node, string key) => Text(node[key]);

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out string? text) ? text : null;
}
