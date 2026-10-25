using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunate.Extensibility;

/// <summary>
/// Reads, writes and hashes <c>extension.lock.json</c>. The document has two halves: the
/// <c>graph</c> — the sources and the packages, with their exact versions, hashes, assets and
/// dependency edges — and the <c>toolset</c> — the framework, RID, runtime and the reference pack
/// with its hash. The writer is canonical: sources, packages, dependencies, files and assets are
/// ordered here, so the same resolution always serializes byte-identically and its hash is a stable
/// identity. The graph's canonical bytes are what the trust digest covers (see
/// <see cref="GraphBytes"/>); the toolset carries its own integrity hash and is not a trust input,
/// so a runtime patch rebuilds without a question. A write goes to a temporary file in the same
/// directory and is moved into place, so a reader never sees a half-written lock.
/// </summary>
public static class ExtensionLockJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// One lock file's text, read once: the graph bytes the trust digest covers, the lock when it is
    /// usable, and the one-line reason it is not — a refused toolset block keeps its graph bytes,
    /// because the graph is still what the user approved and is still what a rebuild compares.
    /// </summary>
    internal sealed record Reading(byte[] GraphBytes, ExtensionLock? Lock, string? Problem);

    /// <summary>The canonical JSON text of <paramref name="lock"/>, with a trailing newline.</summary>
    public static string Write(ExtensionLock @lock)
    {
        ArgumentNullException.ThrowIfNull(@lock);

        JsonObject document = GraphDocument(
            @lock.ExtensionId,
            @lock.ExtensionVersion,
            @lock.SourceDigest,
            @lock.PackagesWritten,
            @lock.Sources,
            @lock.Packages
        );
        document["toolset"] = ToolsetDocument(@lock.Toolset);
        return document.ToJsonString(Indented) + "\n";
    }

    /// <summary>
    /// The canonical bytes of the lock's graph half — everything the trust digest covers, with the
    /// toolset omitted: the id, version, source digest, declared packages, sources and the resolved
    /// packages. Two locks that resolved the same graph for two toolsets have exactly these bytes,
    /// which is what keeps a .NET patch from asking the user anything.
    /// </summary>
    public static byte[] GraphBytes(ExtensionLock @lock)
    {
        ArgumentNullException.ThrowIfNull(@lock);

        return Utf8(
            GraphDocument(
                    @lock.ExtensionId,
                    @lock.ExtensionVersion,
                    @lock.SourceDigest,
                    @lock.PackagesWritten,
                    @lock.Sources,
                    @lock.Packages
                )
                .ToJsonString(Indented) + "\n"
        );
    }

    /// <summary>The lock's canonical bytes, UTF-8 without a byte-order mark.</summary>
    public static byte[] ToBytes(ExtensionLock @lock) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Write(@lock));

    /// <summary>
    /// Parses lock text. A shape this version cannot read, or a toolset block whose integrity hash
    /// does not match its contents, yields null and a one-line reason: the caller reports it instead
    /// of acting on a resolution nobody can vouch for.
    /// </summary>
    public static ExtensionLock? TryParse(string json, out string? error)
    {
        Reading reading = Read(json);
        error = reading.Problem;
        return reading.Lock;
    }

    /// <summary>
    /// Reads lock text once into the digest's input and the lock it can use. A file whose toolset
    /// block is missing, incomplete or modified yields no lock but keeps its graph bytes; only a file
    /// that is not a lock at all yields neither.
    /// </summary>
    internal static Reading Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            return new Reading([], null, $"the lock file is not valid JSON: {exception.Message}");
        }

        if (node is not JsonObject document)
        {
            return new Reading([], null, "the lock file is not a JSON object");
        }

        if (Integer(document, "lockVersion") is not { } version)
        {
            return new Reading([], null, "the lock file has no lockVersion");
        }

        if (version != ExtensionLock.CurrentVersion)
        {
            return new Reading(
                [],
                null,
                FormattableString.Invariant(
                    $"the lock file has lockVersion {version}, but this Lunate writes {ExtensionLock.CurrentVersion}"
                )
            );
        }

        if (
            document["extension"] is not JsonObject extension
            || document["source"] is not JsonObject source
            || document["graph"] is not JsonObject graph
            || graph["packages"] is not JsonArray packages
        )
        {
            return new Reading(
                [],
                null,
                "the lock file is missing extension, source, graph or graph.packages"
            );
        }

        string? id = Text(extension, "id");
        string? extensionVersion = Text(extension, "version");
        string? digest = Text(source, "digest");
        if (id is null || extensionVersion is null || digest is null)
        {
            return new Reading([], null, "the lock file is missing a required field");
        }

        List<ExtensionLockPackage> resolved = [];
        foreach (JsonNode? entry in packages)
        {
            if (entry is not JsonObject package)
            {
                return new Reading([], null, "a locked package is not a JSON object");
            }

            ExtensionLockPackage? locked = Package(package, out string? packageError);
            if (locked is null)
            {
                return new Reading([], null, packageError);
            }

            resolved.Add(locked);
        }

        List<string> packagesWritten = Strings(source["packages"]);
        List<string> sources = Strings(graph["sources"]);
        byte[] graphBytes = Utf8(
            GraphDocument(id, extensionVersion, digest, packagesWritten, sources, resolved)
                .ToJsonString(Indented) + "\n"
        );

        (ExtensionLockToolset? toolset, string? problem) = Toolset(document["toolset"]);
        if (toolset is null)
        {
            return new Reading(graphBytes, null, problem);
        }

        return new Reading(
            graphBytes,
            new ExtensionLock
            {
                ExtensionId = id,
                ExtensionVersion = extensionVersion,
                SourceDigest = digest,
                PackagesWritten = packagesWritten,
                Toolset = toolset,
                Sources = sources,
                Packages = resolved,
            },
            null
        );
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

    /// <summary>
    /// The SHA-256 of the whole lock's bytes, hex lower: the build cache's identity for the
    /// resolution, so a graph change and a toolset change both produce a new key. It is not the
    /// trust digest — that covers the graph half only.
    /// </summary>
    public static string Hash(ReadOnlySpan<byte> lockBytes) =>
        Convert.ToHexStringLower(SHA256.HashData(lockBytes));

    private static JsonObject GraphDocument(
        string extensionId,
        string extensionVersion,
        string sourceDigest,
        IReadOnlyList<string> packagesWritten,
        IReadOnlyList<string> sources,
        IReadOnlyList<ExtensionLockPackage> packages
    )
    {
        var resolved = new JsonArray();
        foreach (ExtensionLockPackage package in Ordered(packages))
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

            resolved.Add(
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

        return new JsonObject
        {
            ["lockVersion"] = ExtensionLock.CurrentVersion,
            ["extension"] = new JsonObject { ["id"] = extensionId, ["version"] = extensionVersion },
            ["source"] = new JsonObject
            {
                ["digest"] = sourceDigest,
                ["packages"] = Strings(packagesWritten),
            },
            ["graph"] = new JsonObject { ["sources"] = Strings(sources), ["packages"] = resolved },
        };
    }

    /// <summary>
    /// The toolset block, with its integrity hash last: the hash covers every field above it, so a
    /// hand edit to the block is refused instead of believed.
    /// </summary>
    private static JsonObject ToolsetDocument(ExtensionLockToolset toolset)
    {
        JsonObject content = ToolsetContent(toolset);
        content["integrity"] = Integrity(content);
        return content;
    }

    private static JsonObject ToolsetContent(ExtensionLockToolset toolset)
    {
        var referencePack = new JsonObject
        {
            ["id"] = toolset.ReferencePack.Id,
            ["version"] = toolset.ReferencePack.Version,
            ["rule"] = toolset.ReferencePack.Rule,
        };
        if (toolset.ReferencePack.Sha512 is { Length: > 0 } sha512)
        {
            referencePack["sha512"] = sha512;
        }

        referencePack["assets"] = Strings(toolset.ReferencePack.Assets);
        return new()
        {
            ["tfm"] = toolset.Tfm,
            ["rid"] = toolset.Rid,
            ["runtime"] = toolset.Runtime,
            ["referencePack"] = referencePack,
        };
    }

    /// <summary>The toolset block's integrity hash: hex lower SHA-256 of its canonical text.</summary>
    private static string Integrity(JsonObject content) =>
        Convert.ToHexStringLower(SHA256.HashData(Utf8(content.ToJsonString(Indented))));

    private static (ExtensionLockToolset? Toolset, string? Problem) Toolset(JsonNode? node)
    {
        if (node is not JsonObject block || block["referencePack"] is not JsonObject pack)
        {
            return (
                null,
                "the lock file's toolset block is missing or unreadable; it is rebuilt before the next build"
            );
        }

        string? tfm = Text(block, "tfm");
        string? rid = Text(block, "rid");
        string? runtime = Text(block, "runtime");
        string? integrity = Text(block, "integrity");
        string? packId = Text(pack, "id");
        string? packVersion = Text(pack, "version");
        string? packRule = Text(pack, "rule");
        string? packSha512 = Text(pack, "sha512");
        if (
            tfm is null
            || rid is null
            || runtime is null
            || integrity is null
            || packId is null
            || packVersion is null
            || packRule is null
        )
        {
            return (
                null,
                "the lock file's toolset block is incomplete; it is rebuilt before the next build"
            );
        }

        var toolset = new ExtensionLockToolset
        {
            Tfm = tfm,
            Rid = rid,
            Runtime = runtime,
            ReferencePack = new ExtensionLockReferencePack
            {
                Id = packId,
                Version = packVersion,
                Rule = packRule,
                Sha512 = packSha512,
                Assets = Strings(pack["assets"]),
            },
        };

        // The block answers for itself: a field edited without recomputing the hash is refused,
        // never used. It is not a trust decision, so the next restore simply rebuilds it.
        return string.Equals(
            integrity,
            Integrity(ToolsetContent(toolset)),
            StringComparison.Ordinal
        )
            ? (toolset, null)
            : (
                null,
                "the lock file's toolset block was modified after it was written (its integrity hash "
                    + "does not match); the resolution is rebuilt before the next build"
            );
    }

    private static ExtensionLockPackage? Package(JsonObject package, out string? error)
    {
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

        error = null;
        return new ExtensionLockPackage
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
        };
    }

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

    private static byte[] Utf8(string text) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    private static string? Text(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out string? text) ? text : null;

    private static int? Integer(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<int>(out int number) ? number : null;
}
