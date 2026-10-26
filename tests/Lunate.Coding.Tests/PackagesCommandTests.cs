using System.Security.Cryptography;
using System.Text.Json;
using Lunate.Extensibility;
using Lunate.Extensibility.Restore;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>lunate packages</c> view (spec: "The <c>lunate packages</c> view"): one line per installed
/// package ordered by scope then id, the documented empty text, <c>--json</c> carrying the same
/// fields plus <c>installedAt</c>, <c>verified</c> and the changed and missing counts, and exit 3
/// when <c>--verify</c> finds a record that does not answer for itself.
/// </summary>
public sealed class PackagesCommandTests
{
    private const string Installed = "2026-01-02T03:04:05.0000000+00:00";

    [Fact]
    public void With_nothing_installed_it_prints_the_documented_text_and_exits_zero()
    {
        using var temp = new TempDirectory();
        Store store = new(temp);

        Result plain = store.Run(store.Worktree);
        Result json = store.Run(store.Worktree, "--json");

        Assert.Equal(PackagesCommand.ExitOk, plain.ExitCode);
        Assert.Empty(plain.Errors);
        Assert.Equal("packages: none installed\n", plain.Output);
        Assert.Equal(PackagesCommand.ExitOk, json.ExitCode);
        Assert.Equal("[]\n", json.Output);
    }

    [Fact]
    public void It_lists_the_user_scope_before_the_project_scope()
    {
        using var temp = new TempDirectory();
        Store store = new(temp);
        store.Install(
            "Beta.Skills",
            "2.0.0",
            PackageScope.User,
            files: [("skills/alpha/SKILL.md", "alpha")],
            worktree: null
        );
        store.Install(
            "Acme.Toolkit",
            "1.4.2",
            PackageScope.User,
            files:
            [
                ("extensions/acme-tools/acme-tools.dll", "assembly"),
                ("skills/greek/SKILL.md", "greek"),
                ("skills/roman/SKILL.md", "roman"),
            ],
            worktree: null
        );
        store.Install(
            "Zeta.Toolkit",
            "1.4.2",
            PackageScope.Project,
            files:
            [
                (".lunate/extensions/acme-tools/acme-tools.dll", "assembly"),
                (".agents/skills/greek/SKILL.md", "greek"),
            ],
            worktree: store.Worktree
        );

        Result result = store.Run(store.Worktree);

        Assert.Equal(PackagesCommand.ExitOk, result.ExitCode);
        Assert.Equal(
            """
            packages: Acme.Toolkit 1.4.2 — user, extension acme-tools 0.1.0, skills greek, roman
            packages: Beta.Skills 2.0.0 — user, skills alpha
            packages: Zeta.Toolkit 1.4.2 — project, extension acme-tools 0.1.0, skills greek

            """.ReplaceLineEndings("\n"),
            result.Output
        );
    }

    [Fact]
    public void The_json_object_carries_the_line_s_fields_plus_installed_at_and_the_counts()
    {
        using var temp = new TempDirectory();
        Store store = new(temp);
        string recordPath = store.Install(
            "Acme.Toolkit",
            "1.4.2",
            PackageScope.User,
            files:
            [
                ("extensions/acme-tools/acme-tools.dll", "assembly"),
                ("skills/greek/SKILL.md", "greek"),
            ],
            worktree: null
        );

        Result result = store.Run(store.Worktree, "--json", "--verify");

        Assert.Equal(PackagesCommand.ExitOk, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement entry = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("Acme.Toolkit", entry.GetProperty("id").GetString());
        Assert.Equal("1.4.2", entry.GetProperty("version").GetString());
        Assert.Equal("user", entry.GetProperty("scope").GetString());
        Assert.Equal("acme-tools", entry.GetProperty("extension").GetProperty("id").GetString());
        Assert.Equal("0.1.0", entry.GetProperty("extension").GetProperty("version").GetString());
        Assert.Equal(
            ["greek"],
            [.. entry.GetProperty("skills").EnumerateArray().Select(item => item.GetString())]
        );
        Assert.Equal(Installed, entry.GetProperty("installedAt").GetString());
        Assert.Equal(recordPath, entry.GetProperty("record").GetString());
        Assert.False(entry.GetProperty("broken").GetBoolean());
        Assert.True(entry.GetProperty("verified").GetBoolean());
        Assert.Equal(0, entry.GetProperty("changed").GetInt32());
        Assert.Equal(0, entry.GetProperty("missing").GetInt32());
        Assert.Null(entry.GetProperty("error").GetString());
        Assert.Equal(1, result.Output.Split('\n').Length - 1);
    }

    [Fact]
    public void A_project_install_from_another_worktree_is_not_listed()
    {
        using var temp = new TempDirectory();
        Store store = new(temp);
        string other = Path.Combine(temp.Root, "other-worktree");
        Directory.CreateDirectory(other);
        store.Install(
            "Acme.Toolkit",
            "1.4.2",
            PackageScope.Project,
            files: [(".lunate/extensions/acme-tools/acme-tools.dll", "assembly")],
            worktree: other
        );

        Result here = store.Run(store.Worktree);
        Result there = store.Run(other);

        Assert.Equal("packages: none installed\n", here.Output);
        Assert.Contains("Acme.Toolkit 1.4.2", there.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_reports_a_changed_file_and_exits_three()
    {
        using var temp = new TempDirectory();
        Store store = new(temp);
        store.Install(
            "Acme.Toolkit",
            "1.4.2",
            PackageScope.User,
            files:
            [
                ("extensions/acme-tools/acme-tools.dll", "assembly"),
                ("skills/greek/SKILL.md", "greek"),
            ],
            worktree: null
        );
        File.WriteAllText(
            Path.Combine(store.StoreRoot, "extensions", "acme-tools", "acme-tools.dll"),
            "edited"
        );

        Result result = store.Run(store.Worktree, "--verify");

        Assert.Equal(PackagesCommand.ExitVerificationFailed, result.ExitCode);
        Assert.Equal(
            """
            packages: Acme.Toolkit 1.4.2 — user, extension acme-tools 0.1.0, skills greek, 1 changed, 0 missing
              changed: extensions/acme-tools/acme-tools.dll
            """.ReplaceLineEndings("\n") + "\n",
            result.Output
        );
    }

    [Fact]
    public void Verify_reports_a_missing_file_and_exits_three()
    {
        using var temp = new TempDirectory();
        Store store = new(temp);
        store.Install(
            "Acme.Toolkit",
            "1.4.2",
            PackageScope.User,
            files: [("skills/greek/SKILL.md", "greek")],
            worktree: null
        );
        File.Delete(Path.Combine(store.StoreRoot, "skills", "greek", "SKILL.md"));

        Result result = store.Run(store.Worktree, "--json", "--verify");

        Assert.Equal(PackagesCommand.ExitVerificationFailed, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement entry = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(0, entry.GetProperty("changed").GetInt32());
        Assert.Equal(1, entry.GetProperty("missing").GetInt32());
        Assert.False(entry.GetProperty("verified").GetBoolean());
    }

    [Fact]
    public void Without_verify_nothing_is_hashed_and_a_changed_file_stays_silent()
    {
        using var temp = new TempDirectory();
        Store store = new(temp);
        store.Install(
            "Acme.Toolkit",
            "1.4.2",
            PackageScope.User,
            files: [("extensions/acme-tools/acme-tools.dll", "assembly")],
            worktree: null
        );
        File.WriteAllText(
            Path.Combine(store.StoreRoot, "extensions", "acme-tools", "acme-tools.dll"),
            "edited"
        );

        Result plain = store.Run(store.Worktree);
        Result json = store.Run(store.Worktree, "--json");

        Assert.Equal(PackagesCommand.ExitOk, plain.ExitCode);
        Assert.DoesNotContain("changed", plain.Output, StringComparison.Ordinal);
        Assert.Equal(
            "packages: Acme.Toolkit 1.4.2 — user, extension acme-tools 0.1.0\n",
            plain.Output
        );
        using JsonDocument document = JsonDocument.Parse(json.Output);
        JsonElement entry = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("verified").ValueKind);
        Assert.Equal(0, entry.GetProperty("changed").GetInt32());
    }

    [Fact]
    public void A_hand_edited_record_reads_broken_and_the_command_names_the_file()
    {
        using var temp = new TempDirectory();
        Store store = new(temp);
        string recordPath = store.Install(
            "Acme.Toolkit",
            "1.4.2",
            PackageScope.User,
            files: [("skills/greek/SKILL.md", "greek")],
            worktree: null
        );
        File.WriteAllText(
            recordPath,
            File.ReadAllText(recordPath).Replace("\"1.4.2\"", "\"1.4.3\"", StringComparison.Ordinal)
        );

        Result plain = store.Run(store.Worktree, "--json");
        Result verify = store.Run(store.Worktree, "--verify");

        Assert.Equal(PackagesCommand.ExitOk, plain.ExitCode);
        using JsonDocument document = JsonDocument.Parse(plain.Output);
        JsonElement entry = Assert.Single(document.RootElement.EnumerateArray());
        Assert.True(entry.GetProperty("broken").GetBoolean());
        Assert.Equal("acme.toolkit", entry.GetProperty("id").GetString());
        Assert.Null(entry.GetProperty("version").GetString());
        Assert.Equal(recordPath, entry.GetProperty("record").GetString());
        Assert.Contains(
            "modified",
            entry.GetProperty("error").GetString()!,
            StringComparison.Ordinal
        );

        Assert.Equal(PackagesCommand.ExitVerificationFailed, verify.ExitCode);
        Assert.Contains("packages: acme.toolkit — broken", verify.Output, StringComparison.Ordinal);
        Assert.Contains(recordPath, verify.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_store_that_does_not_exist_yet_reports_nothing()
    {
        using var temp = new TempDirectory();
        Store store = new(temp);
        string missing = Path.Combine(temp.Root, "no-such-store");

        Result result = store.RunWith(missing, store.Worktree);
        Result json = store.RunWith(missing, store.Worktree, "--json");

        Assert.Equal(PackagesCommand.ExitOk, result.ExitCode);
        Assert.Equal("packages: none installed\n", result.Output);
        Assert.Equal("[]\n", json.Output);
    }

    [Fact]
    public void A_store_that_cannot_be_read_exits_one_with_the_reason_on_stderr()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip(
                "file permissions are POSIX-only; Windows has no equivalent of a directory nothing may read"
            );
            return;
        }
        using var temp = new TempDirectory();
        Store store = new(temp);
        store.Install(
            "Acme.Toolkit",
            "1.4.2",
            PackageScope.User,
            files: [("skills/greek/SKILL.md", "greek")],
            worktree: null
        );
        string packages = Path.Combine(store.StoreRoot, "packages");
        File.SetUnixFileMode(packages, UnixFileMode.None);
        try
        {
            Result result = store.Run(store.Worktree);

            Assert.Equal(PackagesCommand.ExitFailure, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.StartsWith("lunate: ", result.Errors, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(
                packages,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    [Fact]
    public void The_cli_dispatches_packages_to_the_command()
    {
        using var temp = new TempDirectory();
        Store store = new(temp);
        StringWriter output = new();
        StringWriter errors = new();

        int exitCode = Cli.Run(
            ["packages"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store.StoreRoot },
            workingDirectory: store.Worktree
        );

        Assert.Equal(PackagesCommand.ExitOk, exitCode);
        Assert.Equal("packages: none installed\n", output.ToString());
        Assert.Empty(errors.ToString());
    }

    [Fact]
    public void A_usage_error_exits_two_and_prints_the_usage_on_stderr()
    {
        using var temp = new TempDirectory();
        Store store = new(temp);

        Result result = store.Run(store.Worktree, "--update");

        Assert.Equal(PackagesCommand.ExitUsage, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(PackagesCommand.Usage + "\n", result.Errors);
    }

    /// <summary>A store and a worktree for one test, with records written the way an install writes them.</summary>
    private sealed class Store
    {
        private readonly string _root;

        internal Store(TempDirectory temp)
        {
            _root = temp.Root;
            StoreRoot = Path.Combine(_root, "store");
            Worktree = Path.Combine(_root, "worktree");
            Directory.CreateDirectory(StoreRoot);
            Directory.CreateDirectory(Worktree);
        }

        internal string StoreRoot { get; }

        internal string Worktree { get; }

        /// <summary>
        /// An installed package: the files under the scope root, the record under the state root. The
        /// extension id and the skill names are read off the paths, so a fixture says what it means.
        /// </summary>
        internal string Install(
            string id,
            string version,
            PackageScope scope,
            IReadOnlyList<(string Path, string Content)> files,
            string? worktree
        )
        {
            string scopeRoot = scope == PackageScope.Project ? worktree! : StoreRoot;
            List<InstalledFile> installed = [];
            foreach ((string path, string content) in files)
            {
                string full = Path.Combine(
                    scopeRoot,
                    path.Replace('/', Path.DirectorySeparatorChar)
                );
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
                installed.Add(
                    new InstalledFile(
                        path,
                        new FileInfo(full).Length,
                        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(full)))
                    )
                );
            }

            string recordPath = Path.Combine(
                StoreRoot,
                "packages",
                id.ToLowerInvariant(),
                PackageInstallRecordJson.FileName
            );
            PackageInstallRecordJson.WriteTo(
                recordPath,
                new PackageInstallRecord
                {
                    Package = new InstalledPackage
                    {
                        Id = id,
                        Version = version,
                        Sha512 = "c2hhNTEy",
                        Sources = ["local"],
                        Files = [.. files.Select(file => file.Path)],
                        ExtensionId = ExtensionId(files),
                    },
                    Content = new InstalledContent
                    {
                        ExtensionId = ExtensionId(files),
                        ExtensionVersion = ExtensionId(files) is null ? null : "0.1.0",
                        Skills = Skills(files),
                        Paths = installed,
                    },
                    Scope = scope,
                    Worktree = worktree,
                    InstalledAt = Installed,
                }
            );
            return recordPath;
        }

        internal Result Run(string workingDirectory, params string[] args) =>
            RunWith(StoreRoot, workingDirectory, args);

        internal Result RunWith(string storeRoot, string workingDirectory, params string[] args)
        {
            StringWriter output = new();
            StringWriter errors = new();
            int exitCode = PackagesCommand.Run(
                args,
                workingDirectory,
                output,
                errors,
                new ExtensionSeams { StorePath = storeRoot }
            );
            return new Result(exitCode, output.ToString(), errors.ToString());
        }

        /// <summary>The extension id a fixture's paths carry: the directory an <c>extensions/</c> file sits in.</summary>
        private static string? ExtensionId(IReadOnlyList<(string Path, string Content)> files)
        {
            foreach ((string path, _) in files)
            {
                string[] segments = path.Split('/');
                int index = Array.IndexOf(segments, "extensions");
                if (index >= 0 && index + 1 < segments.Length)
                {
                    return segments[index + 1];
                }
            }

            return null;
        }

        private static IReadOnlyList<string> Skills(
            IReadOnlyList<(string Path, string Content)> files
        )
        {
            SortedSet<string> names = new(StringComparer.Ordinal);
            foreach ((string path, _) in files)
            {
                string[] segments = path.Split('/');
                int index = Array.IndexOf(segments, "skills");
                if (index >= 0 && index + 1 < segments.Length)
                {
                    names.Add(segments[index + 1]);
                }
            }

            return [.. names];
        }
    }

    private sealed record Result(int ExitCode, string Output, string Errors);
}
