using System.IO.Compression;
using System.Text;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// A package under construction, written as a real nupkg into a local folder feed (the layout
/// <see cref="TestPackageFeed"/> uses): a nuspec NuGet itself parses, and exactly the entries a test
/// names — no extra metadata, so an assertion about the archive's contents means what it says.
/// </summary>
internal sealed class TestPackage
{
    internal const string FullManifest =
        """{"schema":1,"extension":"extension","skills":"skills"}""";

    internal const string SkillsManifest = """{"schema":1,"skills":"skills"}""";

    private readonly string _id;
    private readonly string _version;
    private readonly string _feed;
    private readonly List<(string Path, byte[] Content)> _files = [];
    private readonly List<string> _dependencies = [];

    internal TestPackage(string root, string id = "Acme.Toolkit", string version = "1.4.2")
    {
        _id = id;
        _version = version;
        _feed = Path.Combine(root, "feed");
        Directory.CreateDirectory(_feed);
    }

    /// <summary>The package manifest at the archive root.</summary>
    internal TestPackage Manifest(string json) => File("lunate.package.json", json);

    /// <summary>A packaged extension folder: a valid manifest and its entry assembly.</summary>
    internal TestPackage Extension(
        string id = "acme-tools",
        string version = "0.1.0",
        string apiVersion = "1.11.0",
        string entryAssembly = "acme-tools.dll"
    )
    {
        File(
            "extension/extension.json",
            $$"""
            {
              "id": "{{id}}",
              "version": "{{version}}",
              "apiVersion": "{{apiVersion}}",
              "entryAssembly": "{{entryAssembly}}"
            }
            """
        );
        File($"extension/{entryAssembly}", "assembly bytes");
        return this;
    }

    /// <summary>One skill folder; <paramref name="name"/> in the frontmatter defaults to the folder.</summary>
    internal TestPackage Skill(string directory, string? name = null)
    {
        File(
            $"skills/{directory}/SKILL.md",
            $"""
            ---
            name: {name ?? directory}
            description: The {directory} skill.
            ---

            Body of {directory}.
            """
        );
        return this;
    }

    /// <summary>One NuGet dependency the nuspec declares, as <c>[1.0.0, )</c>.</summary>
    internal TestPackage DependsOn(params string[] ids)
    {
        _dependencies.AddRange(ids);
        return this;
    }

    internal TestPackage File(string path, string content)
    {
        _files.Add((path, Encoding.UTF8.GetBytes(content)));
        return this;
    }

    /// <summary>The nupkg's path in the feed: <c>&lt;id&gt;.&lt;version&gt;.nupkg</c>.</summary>
    internal string Save()
    {
        string path = Path.Combine(_feed, FormattableString.Invariant($"{_id}.{_version}.nupkg"));
        using FileStream stream = System.IO.File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        Write(
            zip,
            FormattableString.Invariant($"{_id}.nuspec"),
            Nuspec(_id, _version, _dependencies)
        );
        foreach ((string entry, byte[] content) in _files)
        {
            WriteBytes(zip, entry, content);
        }

        return path;
    }

    /// <summary>A nupkg whose entries a test writes itself, for names a builder would refuse.</summary>
    internal static string Raw(
        string root,
        Action<ZipArchive> fill,
        string name = "Acme.Toolkit.1.4.2.nupkg"
    )
    {
        string feed = Path.Combine(root, "feed");
        Directory.CreateDirectory(feed);
        string path = Path.Combine(feed, name);
        using FileStream stream = System.IO.File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        fill(zip);
        return path;
    }

    internal static void Write(ZipArchive zip, string path, string content) =>
        WriteBytes(zip, path, Encoding.UTF8.GetBytes(content));

    internal static void WriteBytes(ZipArchive zip, string path, byte[] content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using Stream stream = entry.Open();
        stream.Write(content);
    }

    /// <summary>
    /// A copy of <paramref name="nupkgPath"/> at <paramref name="destination"/> whose entry
    /// <paramref name="entryName"/> has its compressed bytes replaced, so reading that entry fails the
    /// way a damaged archive does — an extraction failure that is not a missing file.
    /// </summary>
    internal static void CorruptEntry(string nupkgPath, string entryName, string destination)
    {
        byte[] bytes = System.IO.File.ReadAllBytes(nupkgPath);
        (int data, int length) = EntryData(bytes, entryName);
        if (length < 3)
        {
            throw new InvalidOperationException(
                $"entry '{entryName}' compresses to {length} bytes, too few to corrupt usefully."
            );
        }

        Array.Fill(bytes, (byte)0xFF, data, length);
        System.IO.File.WriteAllBytes(destination, bytes);
    }

    /// <summary>Where one entry's compressed bytes start and how many there are, from the zip's own directory.</summary>
    private static (int Data, int Length) EntryData(byte[] bytes, string entryName)
    {
        int end = -1;
        for (var index = bytes.Length - 22; index >= 0; index--)
        {
            if (ReadUInt32(bytes, index) == 0x06054b50)
            {
                end = index;
                break;
            }
        }

        if (end < 0)
        {
            throw new InvalidOperationException(
                "the archive has no end-of-central-directory record."
            );
        }

        int directory = (int)ReadUInt32(bytes, end + 16);
        int count = ReadUInt16(bytes, end + 10);
        int cursor = directory;
        for (var entry = 0; entry < count; entry++)
        {
            if (ReadUInt32(bytes, cursor) != 0x02014b50)
            {
                throw new InvalidOperationException(
                    FormattableString.Invariant(
                        $"central directory record {entry} is not a header."
                    )
                );
            }

            int compressed = (int)ReadUInt32(bytes, cursor + 20);
            int nameLength = ReadUInt16(bytes, cursor + 28);
            int extraLength = ReadUInt16(bytes, cursor + 30);
            int commentLength = ReadUInt16(bytes, cursor + 32);
            int local = (int)ReadUInt32(bytes, cursor + 42);
            string name = Encoding.UTF8.GetString(bytes, cursor + 46, nameLength);
            if (string.Equals(name, entryName, StringComparison.Ordinal))
            {
                int localName = ReadUInt16(bytes, local + 26);
                int localExtra = ReadUInt16(bytes, local + 28);
                return (local + 30 + localName + localExtra, compressed);
            }

            cursor += 46 + nameLength + extraLength + commentLength;
        }

        throw new InvalidOperationException($"the archive holds no entry '{entryName}'.");
    }

    private static int ReadUInt16(byte[] bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8);

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16))
        | ((uint)bytes[offset + 3] << 24);

    /// <summary>The smallest nuspec NuGet reads, optionally with one dependency group.</summary>
    internal static string Nuspec(
        string id = "Acme.Toolkit",
        string version = "1.4.2",
        IReadOnlyList<string>? dependencies = null
    )
    {
        string declared = dependencies is not { Count: > 0 }
            ? string.Empty
            : $"""
                    <dependencies>
                      <group targetFramework="net10.0">
                        {string.Join(
                            "\n        ",
                            dependencies.Select(dependency =>
                                FormattableString.Invariant(
                                    $"<dependency id=\"{dependency}\" version=\"[1.0.0, )\" />"
                                )
                            )
                        )}
                      </group>
                    </dependencies>
                """;

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <authors>lunate-tests</authors>
                <description>A package for the Lunate package-reader tests.</description>
                {declared}
              </metadata>
            </package>
            """;
    }
}
