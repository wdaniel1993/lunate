namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The install record as a document (spec: "The install record"): the two halves, the integrity
/// hash over its own canonical bytes, byte-identical re-writing, and a hand edit reading
/// <c>broken</c> — including a record whose paths leave the scope root.
/// </summary>
public sealed class PackageInstallRecordTests
{
    [Fact]
    public void A_record_answers_for_its_own_bytes_and_rewriting_it_is_byte_identical()
    {
        PackageInstallRecord record = Record();

        string text = PackageInstallRecordJson.Write(record);
        PackageInstallRecord? read = PackageInstallRecordJson.TryParse(
            text,
            "install.json",
            out string? problem
        );

        Assert.Null(problem);
        Assert.NotNull(read);
        Assert.Equal(text, PackageInstallRecordJson.Write(read));
        Assert.Equal(
            PackageInstallRecordJson.ToBytes(record),
            PackageInstallRecordJson.ToBytes(read)
        );
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.Contains("\"integrity\": \"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_integrity_hash_is_computed_over_the_record_with_that_field_empty()
    {
        string text = PackageInstallRecordJson.Write(Record());
        string integrity = IntegrityOf(text);
        Assert.Equal(64, integrity.Length);

        // The hash covers the document with "integrity": "" in the field's place, so recomputing it
        // from the written text (with the value blanked out) has to produce the recorded value.
        string blanked = text.Replace(
            $"\"integrity\": \"{integrity}\"",
            "\"integrity\": \"\"",
            StringComparison.Ordinal
        );
        Assert.Equal(
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(
                        blanked
                    )
                )
            ),
            integrity
        );
    }

    [Fact]
    public void A_hand_edited_record_reads_broken_and_names_the_file()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Root, "install.json");
        PackageInstallRecordJson.WriteTo(path, Record());
        File.WriteAllText(
            path,
            File.ReadAllText(path).Replace("\"1.4.2\"", "\"1.4.3\"", StringComparison.Ordinal)
        );

        PackageInstallRecord? read = PackageInstallRecordJson.TryRead(path, out string? problem);

        Assert.Null(read);
        Assert.NotNull(problem);
        Assert.Contains(path, problem, StringComparison.Ordinal);
        Assert.Contains("modified", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_path_that_leaves_the_scope_root_is_broken()
    {
        PackageInstallRecord record = Record() with
        {
            Content = Record().Content with
            {
                Paths = [new InstalledFile("../../etc/passwd", 4, "abcd")],
            },
        };

        PackageInstallRecord? read = PackageInstallRecordJson.TryParse(
            PackageInstallRecordJson.Write(record),
            "install.json",
            out string? problem
        );

        Assert.Null(read);
        Assert.NotNull(problem);
        Assert.Contains("../../etc/passwd", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_path_that_is_absolute_is_broken()
    {
        PackageInstallRecord record = Record() with
        {
            Content = Record().Content with
            {
                Paths = [new InstalledFile("/etc/passwd", 4, "abcd")],
            },
        };

        PackageInstallRecord? read = PackageInstallRecordJson.TryParse(
            PackageInstallRecordJson.Write(record),
            "install.json",
            out string? problem
        );

        Assert.Null(read);
        Assert.NotNull(problem);
        Assert.Contains("/etc/passwd", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_record_without_a_worktree_is_broken()
    {
        PackageInstallRecord record = Record() with { Scope = PackageScope.Project };

        PackageInstallRecord? read = PackageInstallRecordJson.TryParse(
            PackageInstallRecordJson.Write(record),
            "install.json",
            out string? problem
        );

        Assert.Null(read);
        Assert.Contains("worktree", problem!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absent_record_is_not_a_problem()
    {
        using var temp = new TempDirectory();

        PackageInstallRecord? read = PackageInstallRecordJson.TryRead(
            Path.Combine(temp.Root, "install.json"),
            out string? problem
        );

        Assert.Null(read);
        Assert.Null(problem);
    }

    /// <summary>A complete record, built by hand so the document's exact bytes are the subject.</summary>
    private static PackageInstallRecord Record() =>
        new()
        {
            Package = new InstalledPackage
            {
                Id = "Acme.Toolkit",
                Version = "1.4.2",
                Sha512 = "c2hhNTEy",
                Sources = ["local", "nuget.org"],
                Files =
                [
                    "lunate.package.json",
                    "skills/greek/SKILL.md",
                    "extension/extension.json",
                ],
                ExtensionId = "acme-tools",
            },
            Content = new InstalledContent
            {
                ExtensionId = "acme-tools",
                ExtensionVersion = "0.1.0",
                Skills = ["greek", "roman"],
                Paths =
                [
                    new InstalledFile("skills/roman/SKILL.md", 12, "bbbb"),
                    new InstalledFile("extensions/acme-tools/extension.json", 10, "aaaa"),
                ],
            },
            Scope = PackageScope.User,
            InstalledAt = "2026-01-02T03:04:05.0000000+00:00",
        };

    private static string IntegrityOf(string text)
    {
        const string marker = "\"integrity\": \"";
        int start = text.LastIndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return text[start..(start + 64)];
    }
}
