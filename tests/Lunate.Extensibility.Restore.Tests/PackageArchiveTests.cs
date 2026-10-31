using System.IO.Compression;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// Path confinement and the size bounds (spec: "A package's layout", design D11): every manifest
/// path and every archive entry stays inside the package root, and an archive that is too large is
/// refused before a single byte is extracted, naming the limit and what the archive actually holds.
/// </summary>
public sealed class PackageArchiveTests
{
    [Fact]
    public void A_manifest_folder_that_is_an_absolute_path_is_refused_naming_it()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"extension":"/etc/extension"}""")
            .Extension()
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("'/etc/extension'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("absolute", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_folder_with_a_parent_segment_is_refused_naming_it()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"skills":"../skills"}""")
            .Skill("greek")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("'../skills'", failure.Message, StringComparison.Ordinal);
        Assert.Contains(
            "resolves outside the package root",
            failure.Message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("extension/../../escape.txt")]
    [InlineData(@"extension\..\..\escape.txt")]
    public void An_archive_entry_outside_the_package_root_is_refused_naming_it(string entry)
    {
        using var temp = new TempDirectory();
        string nupkg = TestPackage.Raw(
            temp.Root,
            zip =>
            {
                TestPackage.Write(zip, "Acme.Toolkit.nuspec", TestPackage.Nuspec());
                TestPackage.Write(zip, "lunate.package.json", TestPackage.FullManifest);
                TestPackage.Write(zip, "extension/extension.json", ValidExtensionJson());
                TestPackage.Write(zip, "extension/acme-tools.dll", "assembly bytes");
                TestPackage.Write(zip, entry, "escaped");
            }
        );

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains($"'{entry}'", failure.Message, StringComparison.Ordinal);
        Assert.Contains(
            "resolves outside the package root",
            failure.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void An_archive_over_the_entry_limit_is_refused_naming_the_limit_and_the_count()
    {
        const int Payload = 20_001;
        using var temp = new TempDirectory();
        string nupkg = TestPackage.Raw(
            temp.Root,
            zip =>
            {
                TestPackage.Write(zip, "Acme.Toolkit.nuspec", TestPackage.Nuspec());
                TestPackage.Write(zip, "lunate.package.json", TestPackage.FullManifest);
                for (var index = 0; index < Payload; index++)
                {
                    TestPackage.Write(
                        zip,
                        FormattableString.Invariant($"payload/file{index}.txt"),
                        "x"
                    );
                }
            }
        );
        long entries = CountEntries(nupkg);

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("20000", failure.Message, StringComparison.Ordinal);
        Assert.Contains(
            FormattableString.Invariant($"{entries}"),
            failure.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void An_archive_over_the_expanded_size_limit_is_refused_naming_the_limit_and_the_size()
    {
        const long Limit = 512L * 1024 * 1024;
        using var temp = new TempDirectory();
        string nupkg = TestPackage.Raw(
            temp.Root,
            zip =>
            {
                TestPackage.Write(zip, "Acme.Toolkit.nuspec", TestPackage.Nuspec());
                TestPackage.Write(zip, "lunate.package.json", TestPackage.FullManifest);
                WriteZeros(zip, "skills/greek/big.bin", Limit + 1);
            }
        );
        long expanded = ExpandedSize(nupkg);
        Assert.True(expanded > Limit, "the fixture did not exceed the size limit");

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("512 MiB", failure.Message, StringComparison.Ordinal);
        Assert.Contains(
            FormattableString.Invariant($"{expanded}"),
            failure.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void An_archive_that_is_not_a_zip_is_refused_naming_the_file()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "feed"));
        string path = Path.Combine(temp.Root, "feed", "Acme.Toolkit.1.4.2.nupkg");
        File.WriteAllText(path, "not a zip archive");

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(path)
        );

        Assert.Contains("Acme.Toolkit.1.4.2.nupkg", failure.Message, StringComparison.Ordinal);
        Assert.Contains("zip", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_zip_without_a_nuspec_is_refused_naming_it()
    {
        using var temp = new TempDirectory();
        string nupkg = TestPackage.Raw(
            temp.Root,
            zip => TestPackage.Write(zip, "lunate.package.json", TestPackage.FullManifest)
        );

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains(".nuspec", failure.Message, StringComparison.Ordinal);
    }

    private static string ValidExtensionJson() =>
        """
            {
              "id": "acme-tools",
              "version": "0.1.0",
              "apiVersion": "^1.12.0",
              "entryAssembly": "acme-tools.dll"
            }
            """;

    private static void WriteZeros(ZipArchive zip, string path, long bytes)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using Stream stream = entry.Open();
        byte[] buffer = new byte[1024 * 1024];
        long remaining = bytes;
        while (remaining > 0)
        {
            stream.Write(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            remaining -= buffer.Length;
        }
    }

    private static long CountEntries(string nupkg)
    {
        using FileStream stream = File.OpenRead(nupkg);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.Count;
    }

    private static long ExpandedSize(string nupkg)
    {
        using FileStream stream = File.OpenRead(nupkg);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.Sum(entry => entry.Length);
    }
}
