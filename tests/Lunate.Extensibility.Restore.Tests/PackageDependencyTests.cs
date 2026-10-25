namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// Dependencies and undeclared folders (spec: "A package's layout", design D12): phase 1 refuses a
/// package that declares a NuGet dependency, naming it, because the walk arrives with phase 4; and
/// the folders a package carries that no manifest kind names are not installed, but are named once
/// so nothing disappears silently.
/// </summary>
public sealed class PackageDependencyTests
{
    [Fact]
    public void A_package_that_declares_a_dependency_is_refused_naming_it()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"extension":"extension"}""")
            .Extension()
            .DependsOn("Acme.Lib")
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("'Acme.Lib'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("dependency", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("phase 4", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Undeclared_folders_are_ignored_and_named_once()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .File("build/Acme.Toolkit.targets", "<Project />")
            .File("buildTransitive/Acme.Toolkit.targets", "<Project />")
            .File("lib/net10.0/Acme.Toolkit.dll", "assembly bytes")
            .Save();

        PackageInfo info = PackageReader.Read(nupkg);

        Assert.Equal(["build", "buildTransitive", "lib"], [.. info.IgnoredFolders]);
        Assert.Equal("acme-tools", info.Extension!.Manifest.Id);
        Assert.Equal(["greek"], [.. info.Skills.Select(skill => skill.Name)]);

        string report = PackageReport.Describe(info).ReplaceLineEndings("\n");
        string ignored = Assert.Single(
            report.Split('\n'),
            line => line.StartsWith("ignored:", StringComparison.Ordinal)
        );
        Assert.Contains("build/", ignored, StringComparison.Ordinal);
        Assert.Contains("buildTransitive/", ignored, StringComparison.Ordinal);
        Assert.Contains("lib/", ignored, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(report, "buildTransitive/"));
        Assert.Equal(1, CountOccurrences(report, "lib/"));
    }

    [Fact]
    public void A_package_without_undeclared_folders_prints_no_ignored_line()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest(TestPackage.FullManifest)
            .Extension()
            .Skill("greek")
            .Save();

        PackageInfo info = PackageReader.Read(nupkg);

        Assert.Empty(info.IgnoredFolders);
        Assert.DoesNotContain(
            PackageReport.Describe(info).ReplaceLineEndings("\n"),
            "ignored:",
            StringComparison.Ordinal
        );
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
