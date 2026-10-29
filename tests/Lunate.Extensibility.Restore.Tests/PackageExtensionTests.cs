namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The extension a package carries (spec: "A package's layout", design D2): the folder's
/// <c>extension.json</c> is judged by the existing manifest parser and the existing API gate, and
/// the entry assembly has to be there, because a package that cannot load is not an install.
/// </summary>
public sealed class PackageExtensionTests
{
    [Fact]
    public void A_packaged_extension_is_read_with_the_existing_parser()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"extension":"extension"}""")
            .Extension(id: "acme-tools", version: "0.1.0", apiVersion: "1.12.0")
            .Save();

        PackageInfo info = PackageReader.Read(nupkg);

        PackageExtension extension = Assert.IsType<PackageExtension>(info.Extension);
        Assert.Equal("extension", extension.Directory);
        Assert.Equal("acme-tools", extension.Manifest.Id);
        Assert.Equal("0.1.0", extension.Manifest.Version);
        Assert.Equal("acme-tools.dll", extension.Manifest.EntryAssembly);
    }

    [Fact]
    public void A_remote_extension_without_an_entry_assembly_is_not_refused_for_one()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"extension":"extension"}""")
            .File(
                "extension/extension.json",
                """
                {
                  "id": "acme-remote",
                  "version": "0.1.0",
                  "apiVersion": "1.12.0",
                  "runtime": { "command": "node", "args": ["index.js"] }
                }
                """
            )
            .File("extension/index.js", "console.log('acme');")
            .Save();

        PackageInfo info = PackageReader.Read(nupkg);

        Assert.Equal("acme-remote", info.Extension!.Manifest.Id);
        Assert.Null(info.Extension.Manifest.EntryAssembly);
    }

    [Fact]
    public void An_extension_folder_without_its_manifest_is_refused_naming_the_folder()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"extension":"extension"}""")
            .File("extension/acme-tools.dll", "assembly bytes")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("extension.json", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'extension'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_extension_manifest_the_existing_parser_refuses_is_refused_with_its_reason()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"extension":"extension"}""")
            .File("extension/extension.json", """{"version":"0.1.0","apiVersion":"1.12.0"}""")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("field 'id' is required", failure.Message, StringComparison.Ordinal);
        Assert.Contains("extension/extension.json", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_extension_this_host_is_too_old_for_is_refused_with_the_api_gate_wording()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"extension":"extension"}""")
            .Extension(apiVersion: "2.0.0")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("requires Lunate ≥ 2.0.0", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_entry_assembly_that_is_not_in_the_folder_is_refused_naming_it()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"extension":"extension"}""")
            .File(
                "extension/extension.json",
                """
                {
                  "id": "acme-tools",
                  "version": "0.1.0",
                  "apiVersion": "1.12.0",
                  "entryAssembly": "acme-tools.dll"
                }
                """
            )
            .File("extension/other.dll", "assembly bytes")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("acme-tools.dll", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'extension/'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_extension_folder_with_no_files_is_refused_naming_it()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest(TestPackage.FullManifest)
            .Skill("greek")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("'extension'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("no files", failure.Message, StringComparison.Ordinal);
    }
}
