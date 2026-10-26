using System.IO.Compression;
using System.Text;

namespace Lunate.Coding.Tests;

/// <summary>
/// A package feed a command test can install from: a folder holding real nupkgs and a
/// <c>nuget.config</c> above the working directory that names it, so <c>lunate install</c> reads the
/// same configuration chain it reads in production and no test touches the network or the user's
/// packages folder.
/// </summary>
internal sealed class PackageFeedFixture : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly List<(string Path, byte[] Content)> _entries = [];

    internal PackageFeedFixture()
    {
        Feed = Path.Combine(_temp.Root, "feed");
        GlobalPackages = Path.Combine(_temp.Root, "packages");
        Worktree = Path.Combine(_temp.Root, "worktree");
        // The store sits where LUNATE_HOME would put it for this profile, so a scan that resolves
        // its own state root and a command handed the seam see one store — the production shape.
        Home = Path.Combine(_temp.Root, "home");
        Store = Path.Combine(Home, ".lunate");
        Directory.CreateDirectory(Feed);
        Directory.CreateDirectory(Worktree);
        Directory.CreateDirectory(Store);
        WriteNuGetConfig();
    }

    internal string Feed { get; }

    internal string GlobalPackages { get; }

    internal string Worktree { get; }

    /// <summary>The profile directory the store lives under; a user install writes nothing else here.</summary>
    internal string Home { get; }

    internal string Store { get; }

    /// <summary>The nupkg this fixture built last, so a test can name its own bytes.</summary>
    internal string Nupkg { get; private set; } = string.Empty;

    /// <summary>A bundle carrying an extension and one skill per name; saved into the feed.</summary>
    internal PackageFeedFixture Bundle(
        string id = "Acme.Toolkit",
        string version = "1.4.2",
        string extensionId = "acme-tools",
        string extensionVersion = "0.1.0",
        string[]? dependencies = null,
        params string[] skills
    )
    {
        _entries.Clear();
        File(
            "lunate.package.json",
            """
            {"schema":1,"extension":"extension","skills":"skills"}
            """
        );
        File(
            "extension/extension.json",
            $$"""
            {
              "id": "{{extensionId}}",
              "version": "{{extensionVersion}}",
              "apiVersion": "1.11.0",
              "entryAssembly": "{{extensionId}}.dll"
            }
            """
        );
        File($"extension/{extensionId}.dll", "assembly bytes");
        foreach (string skill in skills.Length == 0 ? ["greek"] : skills)
        {
            File(
                $"skills/{skill}/SKILL.md",
                $"""
                ---
                name: {skill}
                description: The {skill} skill.
                ---

                Body of {skill}.
                """
            );
        }

        Nupkg = Path.Combine(Feed, $"{id}.{version}.nupkg");
        using FileStream stream = System.IO.File.Create(Nupkg);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        Entry(zip, $"{id}.nuspec", Nuspec(id, version, dependencies));
        foreach ((string path, byte[] content) in _entries)
        {
            ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Fastest);
            using Stream target = entry.Open();
            target.Write(content);
        }

        return this;
    }

    /// <summary>
    /// A dependency package in the same feed: <c>lib/net10.0/&lt;id&gt;.dll</c> and the dependency
    /// group <paramref name="dependencies"/> names, so a bundle's closure has something to resolve.
    /// </summary>
    internal PackageFeedFixture Dependency(
        string id,
        string version = "1.0.0",
        params string[] dependencies
    )
    {
        _entries.Clear();
        _entries.Add(($"lib/net10.0/{id}.dll", Encoding.UTF8.GetBytes($"lib bytes of {id}")));
        string path = Path.Combine(Feed, $"{id}.{version}.nupkg");
        using FileStream stream = System.IO.File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        Entry(zip, $"{id}.nuspec", Nuspec(id, version, dependencies));
        foreach ((string entry, byte[] content) in _entries)
        {
            ZipArchiveEntry zipEntry = zip.CreateEntry(entry, CompressionLevel.Fastest);
            using Stream target = zipEntry.Open();
            target.Write(content);
        }

        return this;
    }

    /// <summary>A hand-placed packaged extension in the user extensions root.</summary>
    internal string HandPlacedExtension(string id, string version = "9.9.9")
    {
        string directory = Path.Combine(Store, "extensions", id);
        Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(
            Path.Combine(directory, "extension.json"),
            $$"""
            {"id":"{{id}}","version":"{{version}}","apiVersion":"^1.0.0","entryAssembly":"{{id}}.dll"}
            """
        );
        System.IO.File.WriteAllText(Path.Combine(directory, $"{id}.dll"), "hand placed");
        return directory;
    }

    internal int Run(string[] args, out string output, out string errors)
    {
        StringWriter outWriter = new();
        StringWriter errorWriter = new();
        int exitCode = InstallCommand.Run(
            args,
            Worktree,
            outWriter,
            errorWriter,
            new ExtensionSeams { StorePath = Store }
        );
        output = outWriter.ToString().ReplaceLineEndings("\n");
        errors = errorWriter.ToString().ReplaceLineEndings("\n");
        return exitCode;
    }

    public void Dispose() => _temp.Dispose();

    private void File(string path, string content) =>
        _entries.Add((path, Encoding.UTF8.GetBytes(content)));

    private void WriteNuGetConfig() =>
        System.IO.File.WriteAllText(
            Path.Combine(Worktree, "nuget.config"),
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{{Feed}}" />
              </packageSources>
              <config>
                <add key="globalPackagesFolder" value="{{GlobalPackages}}" />
              </config>
            </configuration>
            """
        );

    private static void Entry(ZipArchive zip, string path, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using Stream stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string Nuspec(string id, string version, string[]? dependencies = null)
    {
        string declared = dependencies is not { Length: > 0 }
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
                <description>A package for the install command tests.</description>
                {declared}
              </metadata>
            </package>
            """;
    }
}
