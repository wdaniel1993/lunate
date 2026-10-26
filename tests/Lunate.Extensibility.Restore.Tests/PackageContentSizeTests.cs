using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The size an install reports (spec: "Install output and <c>--dry-run</c>"): one spelling for a
/// byte count, and the one pure decision about size — a project install above the threshold warns
/// and names its largest entries, a user install never does.
/// </summary>
public sealed class PackageContentSizeTests
{
    private const long Threshold = PackageContentSize.ProjectWarnThreshold;

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KiB")]
    [InlineData(1536, "1.5 KiB")]
    [InlineData(1024 * 1024, "1 MiB")]
    [InlineData(10 * 1024 * 1024, "10 MiB")]
    [InlineData(13_107_200, "12.5 MiB")]
    [InlineData(1024L * 1024 * 1024, "1 GiB")]
    public void A_byte_count_is_spelled_one_way(long bytes, string expected) =>
        Assert.Equal(expected, PackageContentSize.Format(bytes));

    [Fact]
    public void The_total_is_the_sum_of_the_recorded_files()
    {
        InstalledFile[] files =
        [
            new("extensions/acme-tools/acme-tools.dll", 4096, "sha"),
            new("extensions/acme-tools/Acme.Lib.dll", 1024, "sha"),
            new("skills/greek/SKILL.md", 512, "sha"),
        ];

        Assert.Equal(5632, PackageContentSize.Total(files));
        Assert.Equal(0, PackageContentSize.Total([]));
    }

    [Fact]
    public void Only_a_project_install_above_the_threshold_warns()
    {
        Assert.False(PackageContentSize.WarnsFor(PackageScope.User, Threshold * 4));
        Assert.False(PackageContentSize.WarnsFor(PackageScope.Project, Threshold));
        Assert.True(PackageContentSize.WarnsFor(PackageScope.Project, Threshold + 1));
    }

    [Fact]
    public void A_user_install_never_gets_the_size_warning()
    {
        InstalledFile[] files = [new("extensions/acme-tools/acme-tools.dll", Threshold * 4, "sha")];

        Assert.Null(PackageContentSize.Warning(PackageScope.User, files));
    }

    [Fact]
    public void A_project_install_at_the_threshold_does_not_warn()
    {
        InstalledFile[] files = [new("extensions/acme-tools/acme-tools.dll", Threshold, "sha")];

        Assert.Null(PackageContentSize.Warning(PackageScope.Project, files));
    }

    [Fact]
    public void A_project_install_above_the_threshold_names_the_largest_entries()
    {
        InstalledFile[] files =
        [
            new("skills/greek/SKILL.md", 256, "sha"),
            new("extensions/acme-tools/Acme.Lib.dll", 5 * 1024 * 1024, "sha"),
            new("extensions/acme-tools/acme-tools.dll", 6 * 1024 * 1024, "sha"),
            new("extensions/acme-tools/tiny.txt", 2, "sha"),
        ];

        string warning = Assert.IsType<string>(
            PackageContentSize.Warning(PackageScope.Project, files)
        );

        Assert.Contains("11 MiB of content", warning, StringComparison.Ordinal);
        Assert.Contains(
            "above the 10 MiB this version warns at",
            warning,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "'extensions/acme-tools/acme-tools.dll' (6 MiB), 'extensions/acme-tools/Acme.Lib.dll' (5 MiB), 'skills/greek/SKILL.md' (256 B)",
            warning,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("tiny.txt", warning, StringComparison.Ordinal);
    }
}
