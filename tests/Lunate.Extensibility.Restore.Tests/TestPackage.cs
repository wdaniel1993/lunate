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
