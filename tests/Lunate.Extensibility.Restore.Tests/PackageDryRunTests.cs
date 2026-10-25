using System.Security.Cryptography;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The dry run (spec: "Install output and <c>--dry-run</c>"): reading the archive and printing the
/// identity and the contents writes nothing, and every fact it prints comes from the bytes on disk.
/// </summary>
public sealed class PackageDryRunTests
{
    [Fact]
    public void A_dry_run_prints_the_identity_and_the_contents_and_writes_nothing()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .Skill("roman")
            .Save();
        string[] before = Snapshot(temp.Root);
        string sha512 = Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(nupkg)));

        PackageInfo info = PackageReader.Read(nupkg);
        string report = PackageReport.Describe(info).ReplaceLineEndings("\n");

        Assert.Equal("package: Acme.Toolkit 1.4.2", report.Split('\n')[0]);
        Assert.Contains($"source: {nupkg}", report, StringComparison.Ordinal);
        Assert.Contains($"sha512: {sha512}", report, StringComparison.Ordinal);
        Assert.Contains("extension: acme-tools 0.1.0", report, StringComparison.Ordinal);
        Assert.Contains("skills: greek, roman", report, StringComparison.Ordinal);
        Assert.Contains("dry-run: nothing was written", report, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(temp.Root));
    }

    [Fact]
    public void A_dry_run_of_a_skills_only_package_names_no_extension()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"skills":"skills"}""")
            .Skill("greek")
            .Save();

        string report = PackageReport.Describe(PackageReader.Read(nupkg)).ReplaceLineEndings("\n");

        Assert.Contains("skills: greek", report, StringComparison.Ordinal);
        Assert.DoesNotContain("extension:", report, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored:", report, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dry_run_prints_the_entry_count_and_the_expanded_size()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"skills":"skills"}""")
            .Skill("greek")
            .Save();

        PackageInfo info = PackageReader.Read(nupkg);
        string report = PackageReport.Describe(info).ReplaceLineEndings("\n");

        Assert.Contains(
            FormattableString.Invariant(
                $"archive: {info.Files.Count} files, {info.ExpandedBytes} bytes expanded"
            ),
            report,
            StringComparison.Ordinal
        );
    }

    private static string[] Snapshot(string root) =>
        [
            .. Directory
                .GetFiles(root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal),
        ];
}
