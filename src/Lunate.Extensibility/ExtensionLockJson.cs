using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunate.Extensibility;

/// <summary>
/// Reads, writes and hashes <c>extension.lock.json</c>. The writer is canonical: sources,
/// packages, dependencies, files and assets are ordered here, so the same resolution always
/// serializes byte-identically and its hash is a stable identity. A write goes to a temporary file
/// in the same directory and is moved into place, so a reader never sees a half-written lock.
/// </summary>
public static class ExtensionLockJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The canonical JSON text of <paramref name="lock"/>, with a trailing newline.</summary>
    public static string Write(ExtensionLock @lock)
    {
        ArgumentNullException.ThrowIfNull(@lock);

        var packages = new JsonArray();
        foreach (ExtensionLockPackage package in Ordered(@lock.Packages))
        {
            var dependencies = new JsonArray();
            foreach (
                ExtensionLockDependency dependency in package.Dependencies.OrderBy(
                    entry => entry.Id,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                dependencies.Add(
                    new JsonObject { ["id"] = dependency.Id, ["range"] = dependency.Range }
                );
            }

            packages.Add(
                new JsonObject
                {
                    ["id"] = package.Id,
                    ["version"] = package.Version,
                    ["sha512"] = package.Sha512,
                    ["requested"] = package.Requested,
                    ["direct"] = package.Direct,
                    ["dependencies"] = dependencies,
                    ["files"] = Strings(package.Files),
                    ["assets"] = new JsonObject
                    {
                        ["compile"] = Strings(package.Compile),
                        ["runtime"] = Strings(package.Runtime),
                        ["native"] = Strings(package.Native),
                        ["analyzers"] = Strings(package.Analyzers),
                    },
                    ["generators"] = package.Generators,
                }
            );
        }

        var document = new JsonObject
        {
            ["lockVersion"] = ExtensionLock.CurrentVersion,
            ["extension"] = new JsonObject
            {
                ["id"] = @lock.ExtensionId,
                ["version"] = @lock.ExtensionVersion,
            },
            ["source"] = new JsonObject
            {
                ["digest"] = @lock.SourceDigest,
                ["packages"] = Strings(@lock.PackagesWritten),
            },
            ["toolset"] = new JsonObject
            {
                ["tfm"] = @lock.Toolset.Tfm,
                ["rid"] = @lock.Toolset.Rid,
                ["runtime"] = @lock.Toolset.Runtime,
                ["referencePack"] = new JsonObject
                {
                    ["id"] = @lock.Toolset.ReferencePack.Id,
                    ["version"] = @lock.Toolset.ReferencePack.Version,
                    ["rule"] = @lock.Toolset.ReferencePack.Rule,
                    ["assets"] = Strings(@lock.Toolset.ReferencePack.Assets),
                },
            },
            ["sources"] = Strings(@lock.Sources),
            ["packages"] = packages,
        };

        return document.ToJsonString(Indented) + "\n";
    }

    /// <summary>The lock's canonical bytes, UTF-8 without a byte-order mark.</summary>
    public static byte[] ToBytes(ExtensionLock @lock) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Write(@lock));

    /// <summary>
    /// Parses lock text. A shape this version cannot read yields null and a one-line reason: the
    /// caller reports it instead of acting on a resolution nobody can vouch for.
    /// </summary>
    public static ExtensionLock? TryParse(string json, out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            error = $"the lock file is not valid JSON: {exception.Message}";
            return null;
        }

        if (node is not JsonObject document)
        {
            error = "the lock file is not a JSON object";
            return null;
        }

        if (Integer(document, "lockVersion") is not { } version)
        {
            error = "the lock file has no lockVersion";
            return null;
        }

        if (version != ExtensionLock.CurrentVersion)
        {
            error = FormattableString.Invariant(
                $"the lock file has lockVersion {version}, but this Lunate writes {ExtensionLock.CurrentVersion}"
            );
            return null;
        }

        if (
            document["extension"] is not JsonObject extension
            || document["source"] is not JsonObject source
            || document["toolset"] is not JsonObject toolset
            || toolset["referencePack"] is not JsonObject referencePack
            || document["packages"] is not JsonArray packages
        )
        {
            error = "the lock file is missing extension, source, toolset or packages";
            return null;
        }

        string? id = Text(extension, "id");
        string? extensionVersion = Text(extension, "version");
        string? digest = Text(source, "digest");
        string? tfm = Text(toolset, "tfm");
        string? rid = Text(toolset, "rid");
        string? runtime = Text(toolset, "runtime");
        string? packId = Text(referencePack, "id");
        string? packVersion = Text(referencePack, "version");
        string? packRule = Text(referencePack, "rule");
        if (
            id is null
            || extensionVersion is null
            || digest is null
            || tfm is null
            || rid is null
            || runtime is null
            || packId is null
            || packVersion is null
            || packRule is null
        )
        {
            error = "the lock file is missing a required field";
            return null;
        }

        List<ExtensionLockPackage> resolved = [];
        foreach (JsonNode? entry in packages)
        {
            if (entry is not JsonObject package)
            {
                error = "a locked package is not a JSON object";
                return null;
            }

            string? packageId = Text(package, "id");
            string? packageVersion = Text(package, "version");
            string? sha512 = Text(package, "sha512");
            string? requested = Text(package, "requested");
            if (
                packageId is null
                || packageVersion is null
                || sha512 is null
                || requested is null
                || package["assets"] is not JsonObject assets
            )
            {
                error = "a locked package is missing id, version, sha512, requested or assets";
                return null;
            }

            resolved.Add(
                new ExtensionLockPackage
                {
                    Id = packageId,
                    Version = packageVersion,
                    Sha512 = sha512,
                    Requested = requested,
                    Direct =
                        package["direct"] is JsonValue direct
                        && direct.TryGetValue<bool>(out bool isDirect)
                        && isDirect,
                    Dependencies = Dependencies(package["dependencies"]),
                    Files = Strings(package["files"]),
                    Compile = Strings(assets["compile"]),
                    Runtime = Strings(assets["runtime"]),
                    Native = Strings(assets["native"]),
                    Analyzers = Strings(assets["analyzers"]),
                    Generators =
                        package["generators"] is JsonValue generators
                        && generators.TryGetValue<bool>(out bool hasGenerators)
                        && hasGenerators,
                }
            );
        }

        error = null;
        return new ExtensionLock
        {
            ExtensionId = id,
            ExtensionVersion = extensionVersion,
            SourceDigest = digest,
            PackagesWritten = Strings(source["packages"]),
            Toolset = new ExtensionLockToolset
            {
                Tfm = tfm,
                Rid = rid,
                Runtime = runtime,
                ReferencePack = new ExtensionLockReferencePack
                {
                    Id = packId,
                    Version = packVersion,
                    Rule = packRule,
                    Assets = Strings(referencePack["assets"]),
                },
            },
            Sources = Strings(document["sources"]),
            Packages = resolved,
        };
    }

    /// <summary>
    /// Reads a lock file; null when it is absent. An unreadable or unparsable file yields null and
    /// a reason, which is reported and never silently replaced.
    /// </summary>
    public static ExtensionLock? TryRead(string path, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        error = null;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return TryParse(File.ReadAllText(path), out error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"the lock file could not be read: {exception.Message}";
            return null;
        }
    }

    /// <summary>
    /// Writes the canonical text through a temporary file in the same directory, then moves it into
    /// place, so a concurrent reader sees either the old lock or the new one.
    /// </summary>
    public static void WriteTo(string path, ExtensionLock @lock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(@lock);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temporary, ToBytes(@lock));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The SHA-256 of the lock's bytes, hex lower: the identity bound into the trust record.</summary>
    public static string Hash(ReadOnlySpan<byte> lockBytes) =>
        Convert.ToHexStringLower(SHA256.HashData(lockBytes));

    private static IEnumerable<ExtensionLockPackage> Ordered(
        IReadOnlyList<ExtensionLockPackage> packages
    ) =>
        packages
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Version, StringComparer.Ordinal);

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

    private static List<ExtensionLockDependency> Dependencies(JsonNode? node)
    {
        List<ExtensionLockDependency> dependencies = [];
        if (node is JsonArray array)
        {
            foreach (JsonNode? entry in array)
            {
                if (
                    entry is JsonObject dependency
                    && Text(dependency, "id") is { } id
                    && Text(dependency, "range") is { } range
                )
                {
                    dependencies.Add(new ExtensionLockDependency(id, range));
                }
            }
        }

        return dependencies;
    }

    private static string? Text(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out string? text) ? text : null;

    private static int? Integer(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<int>(out int number) ? number : null;
}
