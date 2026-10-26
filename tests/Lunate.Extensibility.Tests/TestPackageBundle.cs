using System.IO.Compression;
using System.Text;
using Lunate.Extensibility;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// A real bundle in a local folder feed: a nuspec NuGet parses, <c>lunate.package.json</c>, an
/// extension folder built from a TestExtensions output directory, and one skill per name. The
/// installer reads it through NuGet's own code, so a test proves the whole path — download, read,
/// stage, write — against bytes it built itself.
/// </summary>
internal sealed class TestPackageBundle
{
    private readonly string _root;
    private readonly List<(string Path, byte[] Content)> _files = [];

    internal TestPackageBundle(string root)
    {
        _root = root;
        Feed = Path.Combine(root, "feed");
        Directory.CreateDirectory(Feed);
    }

    internal string Feed { get; }

    internal string GlobalPackages => Path.Combine(_root, "packages");

    /// <summary>The extension folder, built from <paramref name="sourceDirectory"/>'s DLLs.</summary>
    internal TestPackageBundle Extension(
        string id,
        string sourceDirectory,
        string entryAssembly,
        string version = "0.1.0",
        string apiVersion = "1.11.0"
    )
    {
        Text(
            "extension/extension.json",
            $$"""
            {"id":"{{id}}","version":"{{version}}","apiVersion":"{{apiVersion}}","entryAssembly":"{{entryAssembly}}"}
            """
        );
        foreach (string file in Directory.GetFiles(sourceDirectory, "*.dll"))
        {
            Binary("extension/" + Path.GetFileName(file), File.ReadAllBytes(file));
        }

        return this;
    }

    internal TestPackageBundle Skill(string name)
    {
        Text(
            $"skills/{name}/SKILL.md",
            $"""
            ---
            name: {name}
            description: The {name} skill.
            ---

            Body of {name}.
            """
        );
        return this;
    }

    /// <summary>Writes the nupkg into the feed and returns its path.</summary>
    internal string Save(string packageId = "Acme.Toolkit", string version = "1.4.2")
    {
        Text("lunate.package.json", """{"schema":1,"extension":"extension","skills":"skills"}""");
        string path = Path.Combine(Feed, $"{packageId}.{version}.nupkg");
        using FileStream stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        Entry(zip, $"{packageId}.nuspec", Encoding.UTF8.GetBytes(Nuspec(packageId, version)));
        foreach ((string entry, byte[] content) in _files)
        {
            Entry(zip, entry, content);
        }

        return path;
    }

    private void Text(string path, string content) =>
        _files.Add((path, Encoding.UTF8.GetBytes(content)));

    private void Binary(string path, byte[] content) => _files.Add((path, content));

    private static void Entry(ZipArchive zip, string path, byte[] content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using Stream stream = entry.Open();
        stream.Write(content);
    }

    private static string Nuspec(string id, string version) =>
        $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <authors>lunate-tests</authors>
                <description>A bundle for the install scope and trust tests.</description>
              </metadata>
            </package>
            """;
}
