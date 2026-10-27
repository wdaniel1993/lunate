using System.Security.Cryptography;
using System.Text.Json;
using Lunate.Extensibility;
using Lunate.Extensibility.Restore;
using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>
/// <c>lunate install</c> (spec: "Install output and <c>--dry-run</c>", "Where installed content
/// lands", "Scope, precedence and collisions"): the identity prints before the one line saying what
/// was written, a user install follows the state root, a project install writes the worktree's
/// committable content, the same version twice reports already-installed, and the refusals a user
/// can act on exit 2 with the usage on stderr and write nothing.
/// </summary>
public sealed class InstallCommandTests
{
    [Fact]
    public void An_install_started_inside_a_session_is_refused_without_yes()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);

        int exit = feed.Run(
            ["Acme.Toolkit@1.4.2"],
            out string output,
            out string errors,
            PackageFeedFixture.InsideSession
        );

        Assert.Equal(InstallCommand.ExitUsage, exit);
        Assert.Empty(output);
        Assert.Contains("--yes", errors, StringComparison.Ordinal);
        Assert.Contains("runs at the next load", errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "packages")));
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "extensions")));
    }

    [Fact]
    public void A_session_install_with_yes_proceeds()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);

        int exit = feed.Run(
            ["Acme.Toolkit@1.4.2", "--yes", "--dry-run"],
            out string output,
            out string errors,
            PackageFeedFixture.InsideSession
        );

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("dry-run: nothing was written", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "packages")));
    }

    [Fact]
    public void Outside_a_session_the_install_is_ungated()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);

        int exit = feed.Run(
            ["Acme.Toolkit@1.4.2"],
            out string output,
            out string errors,
            name => null
        );

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("installed: ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_usage_line_names_yes()
    {
        using var feed = new PackageFeedFixture();

        int exit = feed.Run(["--yes-and-nothing-else"], out _, out string errors);

        Assert.Equal(InstallCommand.ExitUsage, exit);
        Assert.Contains("[--yes]", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Reinstalling_repairs_an_unreadable_record_and_the_view_verifies_clean()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));
        string record = Path.Combine(
            feed.Store,
            "packages",
            "acme.toolkit",
            PackageInstallRecordJson.FileName
        );
        File.WriteAllText(record, "{ this is not a record");
        string leftover = Path.Combine(feed.Store, "extensions", "acme-tools", "old.dll");
        File.WriteAllText(leftover, "bytes a previous version wrote");

        int exit = feed.Run(["Acme.Toolkit@1.4.2"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Contains("installed: ", output, StringComparison.Ordinal);
        // The run says what it could not read and what it therefore did not remove.
        Assert.Contains(record, errors, StringComparison.Ordinal);
        Assert.Contains(leftover, errors, StringComparison.Ordinal);
        Assert.True(File.Exists(leftover));

        using var view = new StringWriter();
        using var viewErrors = new StringWriter();
        int verify = PackagesCommand.Run(
            ["--verify"],
            feed.Worktree,
            view,
            viewErrors,
            new ExtensionSeams { StorePath = feed.Store }
        );
        Assert.Equal(PackagesCommand.ExitOk, verify);
        Assert.Empty(viewErrors.ToString());
        Assert.Contains("Acme.Toolkit 1.4.2", view.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("broken", view.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_install_prints_the_total_content_size_in_both_scopes()
    {
        using var user = new PackageFeedFixture();
        user.Bundle(skills: ["greek"]);
        Assert.Equal(0, user.Run(["Acme.Toolkit@1.4.2"], out string userText, out _));
        long userBytes = Total(user.Store, "extensions/acme-tools", "skills/greek");
        Assert.Contains(
            $"content: {PackageContentSize.Format(userBytes)} in 3 files",
            userText,
            StringComparison.Ordinal
        );

        using var project = new PackageFeedFixture();
        GitTestRepo.Init(project.Worktree);
        project.Bundle(skills: ["greek"]);
        Assert.Equal(
            0,
            project.Run(["Acme.Toolkit@1.4.2", "--project"], out string projectText, out _)
        );
        long projectBytes = Total(
            project.Worktree,
            ".lunate/extensions/acme-tools",
            ".agents/skills/greek"
        );
        Assert.Contains(
            $"content: {PackageContentSize.Format(projectBytes)} in 3 files",
            projectText,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void A_project_install_over_the_threshold_warns_and_names_the_largest_entries()
    {
        using var feed = new PackageFeedFixture();
        GitTestRepo.Init(feed.Worktree);
        feed.Bundle(skills: ["greek"], padBytes: 11L * 1024 * 1024);

        int exit = feed.Run(
            ["Acme.Toolkit@1.4.2", "--project"],
            out string output,
            out string errors
        );

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Contains("content: 11 MiB", output, StringComparison.Ordinal);
        Assert.Contains("above the 10 MiB this version warns at", errors, StringComparison.Ordinal);
        Assert.Contains("pad.bin' (11 MiB)", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void A_user_install_reports_the_total_and_never_warns_about_committing_it()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"], padBytes: 11L * 1024 * 1024);

        int exit = feed.Run(["Acme.Toolkit@1.4.2"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("content: 11 MiB", output, StringComparison.Ordinal);
    }

    /// <summary>The recorded content's total, read from the files the install wrote.</summary>
    private static long Total(string root, params string[] folders) =>
        folders.Sum(folder =>
            Directory
                .EnumerateFiles(
                    Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar)),
                    "*",
                    SearchOption.AllDirectories
                )
                .Sum(file => new FileInfo(file).Length)
        );

    [Fact]
    public void A_user_install_writes_the_state_root_scope_and_prints_the_identity()
    {
        using var feed = new PackageFeedFixture();
        string nupkg = feed.Bundle(skills: ["greek", "roman"]).Nupkg;

        int exit = feed.Run(["Acme.Toolkit@1.4.2"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        string[] lines = output.TrimEnd('\n').Split('\n');
        Assert.Equal("package: Acme.Toolkit 1.4.2", lines[0]);
        Assert.Equal("source: local", lines[1]);
        Assert.Equal(
            "sha512: " + Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(nupkg))),
            lines[2]
        );
        Assert.Contains("extension: acme-tools 0.1.0", output, StringComparison.Ordinal);
        Assert.Contains("skills: greek, roman", output, StringComparison.Ordinal);
        Assert.Contains("scope: user", output, StringComparison.Ordinal);
        Assert.Contains(
            $"installed: 4 files under '{feed.Store}'",
            output,
            StringComparison.Ordinal
        );

        Assert.True(
            File.Exists(Path.Combine(feed.Store, "extensions", "acme-tools", "acme-tools.dll"))
        );
        Assert.True(File.Exists(Path.Combine(feed.Store, "skills", "greek", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(feed.Store, "skills", "roman", "SKILL.md")));
        Assert.True(
            File.Exists(Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json"))
        );
    }

    [Fact]
    public void Install_prints_the_dependency_closure_before_what_it_wrote()
    {
        using var feed = new PackageFeedFixture();
        feed.Dependency("Acme.Lib", "1.0.0", "Acme.Core").Dependency("Acme.Core", "1.2.0");
        feed.Bundle(skills: ["greek"], dependencies: ["Acme.Lib"]);

        int exit = feed.Run(["Acme.Toolkit@1.4.2"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains(
            "dependencies: Acme.Core 1.2.0, Acme.Lib 1.0.0",
            output,
            StringComparison.Ordinal
        );
        Assert.True(
            output.IndexOf("dependencies:", StringComparison.Ordinal)
                < output.IndexOf("installed:", StringComparison.Ordinal),
            "the closure prints before the line saying what was written"
        );
        Assert.True(
            File.Exists(Path.Combine(feed.Store, "extensions", "acme-tools", "Acme.Lib.dll"))
        );
        Assert.True(
            File.Exists(Path.Combine(feed.Store, "extensions", "acme-tools", "Acme.Core.dll"))
        );
    }

    [Fact]
    public void A_dry_run_prints_the_closure_and_resolves_nothing_into_the_store()
    {
        using var feed = new PackageFeedFixture();
        feed.Dependency("Acme.Lib", "1.0.0");
        feed.Bundle(skills: ["greek"], dependencies: ["Acme.Lib"]);

        int exit = feed.Run(
            ["--dry-run", "Acme.Toolkit@1.4.2"],
            out string output,
            out string errors
        );

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("dependencies: Acme.Lib 1.0.0", output, StringComparison.Ordinal);
        Assert.Contains("dry-run: nothing was written", output, StringComparison.Ordinal);

        // Nothing under the state root — not even the lock: a description is not an install. The
        // closure was resolved into the packages folder, which is NuGet's cache and not the store.
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "extensions")));
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "packages")));
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "cache")));
        Assert.True(
            File.Exists(
                Path.Combine(feed.GlobalPackages, "acme.lib", "1.0.0", "acme.lib.1.0.0.nupkg")
            )
        );
    }

    [Fact]
    public void Json_carries_the_dependency_closure()
    {
        using var feed = new PackageFeedFixture();
        feed.Dependency("Acme.Lib", "1.0.0");
        feed.Bundle(skills: ["greek"], dependencies: ["Acme.Lib"]);

        int exit = feed.Run(["Acme.Toolkit@1.4.2", "--json"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement dependency = Assert.Single(
            document.RootElement.GetProperty("dependencies").EnumerateArray()
        );
        Assert.Equal("Acme.Lib", dependency.GetProperty("id").GetString());
        Assert.Equal("1.0.0", dependency.GetProperty("version").GetString());
    }

    [Fact]
    public void A_project_install_writes_the_worktree_and_its_content_is_untracked()
    {
        using var feed = new PackageFeedFixture();
        GitTestRepo.Init(feed.Worktree);
        feed.Bundle(skills: ["greek"]);

        int exit = feed.Run(
            ["Acme.Toolkit@1.4.2", "--project"],
            out string output,
            out string errors
        );

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("scope: project", output, StringComparison.Ordinal);
        Assert.Contains(
            $"worktree: {Workspace.Canonicalize(feed.Worktree)}",
            output,
            StringComparison.Ordinal
        );
        Assert.True(
            File.Exists(
                Path.Combine(feed.Worktree, ".lunate", "extensions", "acme-tools", "acme-tools.dll")
            )
        );
        Assert.True(
            File.Exists(Path.Combine(feed.Worktree, ".agents", "skills", "greek", "SKILL.md"))
        );

        // The record is machine state, under the state root, and names the worktree it belongs to.
        string record = Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json");
        Assert.True(File.Exists(record));
        // Compare the value, not its serialized text: a Windows path arrives with escaped
        // backslashes, so a substring of the raw file is a test of the serializer's escaping
        // rather than of what the record says.
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(record)))
        {
            Assert.Equal(
                Workspace.Canonicalize(feed.Worktree),
                document.RootElement.GetProperty("worktree").GetString()
            );
        }
        Assert.False(Directory.Exists(Path.Combine(feed.Worktree, "packages")));

        // The content is the repository's: git sees it as untracked additions.
        string status = GitTestRepo.Output(feed.Worktree, "status", "--porcelain=v1", "-uall");
        Assert.Contains("?? .lunate/", status, StringComparison.Ordinal);
        Assert.Contains("?? .agents/", status, StringComparison.Ordinal);
        Assert.Contains(
            ".lunate/extensions/acme-tools/acme-tools.dll",
            status,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void An_upgrade_replaces_the_installed_version_and_prints_what_changed()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(version: "1.4.2", skills: ["greek", "roman"]);
        feed.Bundle(version: "1.5.0", skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));

        int exit = feed.Run(["Acme.Toolkit@1.5.0"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("package: Acme.Toolkit 1.5.0", output, StringComparison.Ordinal);
        Assert.Contains("upgraded: 1.4.2 to 1.5.0", output, StringComparison.Ordinal);
        Assert.Contains($"under '{feed.Store}'", output, StringComparison.Ordinal);

        string record = Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json");
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(record)))
        {
            Assert.Equal(
                "1.5.0",
                document.RootElement.GetProperty("package").GetProperty("version").GetString()
            );
        }

        Assert.False(
            Directory.Exists(Path.Combine(feed.Store, "skills", "roman")),
            "a skill the new version does not carry is gone"
        );
        Assert.True(File.Exists(Path.Combine(feed.Store, "skills", "greek", "SKILL.md")));
    }

    [Fact]
    public void A_failed_upgrade_exits_one_and_leaves_the_record_identical()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(version: "1.4.2", skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));
        string record = Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json");
        byte[] before = File.ReadAllBytes(record);

        int exit = feed.Run(["Acme.Toolkit@9.9.9"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitFailure, exit);
        Assert.Empty(output);
        Assert.Contains("9.9.9", errors, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(record));
        Assert.True(File.Exists(Path.Combine(feed.Store, "skills", "greek", "SKILL.md")));
    }

    [Fact]
    public void Force_repairs_a_missing_recorded_file_from_the_recorded_version()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(version: "1.4.2", skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));
        string assembly = Path.Combine(feed.Store, "extensions", "acme-tools", "acme-tools.dll");
        byte[] recorded = File.ReadAllBytes(assembly);
        File.Delete(assembly);
        string record = Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json");
        byte[] recordBefore = File.ReadAllBytes(record);

        int exit = feed.Run(
            ["Acme.Toolkit@1.4.2", "--force"],
            out string output,
            out string errors
        );

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("repaired: 1 of", output, StringComparison.Ordinal);
        Assert.Equal(recorded, File.ReadAllBytes(assembly));
        Assert.Equal(recordBefore, File.ReadAllBytes(record));
    }

    [Fact]
    public void The_same_version_twice_reports_already_installed_and_exits_zero()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));

        int exit = feed.Run(["Acme.Toolkit@1.4.2"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("already installed: 1.4.2 is unchanged", output, StringComparison.Ordinal);
        Assert.Contains(
            $"record '{Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json")}'",
            output,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void A_duplicate_extension_id_is_refused_at_install_naming_both_providers()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle();
        string handPlaced = feed.HandPlacedExtension("acme-tools");

        int exit = feed.Run(["Acme.Toolkit@1.4.2"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitFailure, exit);
        Assert.Empty(output);
        Assert.Contains(handPlaced, errors, StringComparison.Ordinal);
        Assert.Contains("acme-tools", errors, StringComparison.Ordinal);
        Assert.Contains("extension id has one provider", errors, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json"))
        );
    }

    [Fact]
    public void A_range_is_a_usage_error_and_writes_nothing()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle();

        int exit = feed.Run(["Acme.Toolkit@[1.0.0,2.0.0)"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitUsage, exit);
        Assert.Empty(output);
        Assert.Contains(InstallCommand.Usage, errors, StringComparison.Ordinal);
        Assert.Contains("[1.0.0,2.0.0)", errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "packages", "acme.toolkit")));
    }

    [Fact]
    public void A_git_url_is_refused_by_name()
    {
        using var feed = new PackageFeedFixture();

        int exit = feed.Run(
            ["https://github.com/acme/toolkit.git"],
            out string output,
            out string errors
        );

        Assert.Equal(InstallCommand.ExitUsage, exit);
        Assert.Empty(output);
        Assert.Contains("<package-id>[@<version>]", errors, StringComparison.Ordinal);
        Assert.Contains(
            "git installs are not supported in this version",
            errors,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void A_path_is_refused_because_a_directory_is_not_a_distribution_unit()
    {
        using var feed = new PackageFeedFixture();

        int exit = feed.Run(["./Acme.Toolkit.nupkg"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitUsage, exit);
        Assert.Empty(output);
        Assert.Contains("is a path", errors, StringComparison.Ordinal);
        Assert.Contains("not a distribution unit", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_scope_outside_a_repository_is_a_usage_error()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle();

        int exit = feed.Run(
            ["Acme.Toolkit@1.4.2", "--project"],
            out string output,
            out string errors
        );

        Assert.Equal(InstallCommand.ExitUsage, exit);
        Assert.Empty(output);
        Assert.Contains("--project needs a worktree", errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(feed.Worktree, ".lunate")));
    }

    /// <summary>
    /// The refusal above is for a directory outside a repository, not for one below its root: the
    /// repository identity comes from the nearest ancestor <c>.git</c>, so a project install from a
    /// subdirectory is a project install.
    /// </summary>
    [Fact]
    public void Project_scope_from_a_subdirectory_finds_the_repository()
    {
        using var feed = new PackageFeedFixture();
        GitTestRepo.Init(feed.Worktree);
        feed.Bundle(skills: ["greek"]);
        string subdirectory = Path.Combine(feed.Worktree, "src", "App");
        Directory.CreateDirectory(subdirectory);

        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exit = InstallCommand.Run(
            ["Acme.Toolkit@1.4.2", "--project", "--dry-run"],
            subdirectory,
            output,
            errors,
            new ExtensionSeams { StorePath = feed.Store },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors.ToString());
        Assert.Contains("scope: project", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dry_run_prints_the_identity_and_writes_nothing()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);

        int exit = feed.Run(
            ["--dry-run", "Acme.Toolkit@1.4.2"],
            out string output,
            out string errors
        );

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("package: Acme.Toolkit 1.4.2", output, StringComparison.Ordinal);
        Assert.Contains("source: local", output, StringComparison.Ordinal);
        Assert.Contains("extension: acme-tools 0.1.0", output, StringComparison.Ordinal);
        Assert.Contains("skills: greek", output, StringComparison.Ordinal);
        Assert.Contains("dry-run: nothing was written", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "extensions")));
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "packages")));
    }

    [Fact]
    public void Json_carries_the_same_fields()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);

        int exit = feed.Run(["Acme.Toolkit", "--json"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        Assert.Equal("Acme.Toolkit", root.GetProperty("id").GetString());
        Assert.Equal("1.4.2", root.GetProperty("version").GetString());
        Assert.Equal("local", root.GetProperty("source").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("sha512").GetString()));
        Assert.Equal("acme-tools", root.GetProperty("extension").GetProperty("id").GetString());
        Assert.Equal(
            ["greek"],
            [.. root.GetProperty("skills").EnumerateArray().Select(item => item.GetString())]
        );
        Assert.Equal("user", root.GetProperty("scope").GetString());
        Assert.False(root.GetProperty("alreadyInstalled").GetBoolean());
        Assert.False(root.GetProperty("dryRun").GetBoolean());
        Assert.Equal(3, root.GetProperty("written").GetArrayLength());
    }

    [Fact]
    public void A_bare_id_installs_the_latest_stable_version()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(version: "1.4.2");
        feed.Bundle(version: "1.5.0");
        feed.Bundle(version: "2.0.0-beta.1");

        int exit = feed.Run(["Acme.Toolkit"], out string output, out string errors);

        Assert.Equal(InstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("package: Acme.Toolkit 1.5.0", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_user_install_follows_LUNATE_HOME()
    {
        const string Method = nameof(A_user_install_follows_LUNATE_HOME);
        if (TestChildProcess.IsChild(Method))
        {
            // LUNATE_HOME is process-wide, so the install runs in its own process.
            StringWriter text = new();
            StringWriter errors = new();
            int exit = InstallCommand.Run(
                ["Acme.Toolkit@1.4.2"],
                Environment.GetEnvironmentVariable("LUNATE_TEST_INSTALL_WORKTREE"),
                text,
                errors,
                seams: null,
                TestContext.Current.CancellationToken
            );
            Assert.Equal(InstallCommand.ExitOk, exit);
            Assert.Empty(errors.ToString());
            return;
        }

        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);
        string home = Path.GetFullPath(Path.Combine(feed.Home, "..", "redirected-home"));

        (int exitCode, string output) = await TestChildProcess.RunAsync(
            typeof(InstallCommandTests),
            Method,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["LUNATE_HOME"] = home,
                ["LUNATE_TEST_INSTALL_WORKTREE"] = feed.Worktree,
            }
        );

        Assert.True(exitCode == 0, $"the redirected install failed (exit {exitCode}):{output}");
        Assert.True(
            File.Exists(Path.Combine(home, "extensions", "acme-tools", "acme-tools.dll")),
            "the extension did not land under LUNATE_HOME"
        );
        Assert.True(File.Exists(Path.Combine(home, "skills", "greek", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(home, "packages", "acme.toolkit", "install.json")));
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "extensions")));
    }

    [Fact]
    public async Task A_store_inside_the_worktree_is_refused_with_the_guarded_message()
    {
        const string Method = nameof(
            A_store_inside_the_worktree_is_refused_with_the_guarded_message
        );
        if (TestChildProcess.IsChild(Method))
        {
            // LUNATE_HOME is process-wide, so this half runs in its own process: the tests beside
            // this one keep their environment.
            StringWriter text = new();
            StringWriter errors = new();
            int exit = InstallCommand.Run(
                ["Acme.Toolkit@1.4.2"],
                Environment.GetEnvironmentVariable("LUNATE_TEST_INSTALL_WORKTREE"),
                text,
                errors,
                seams: null,
                TestContext.Current.CancellationToken
            );
            Assert.Equal(InstallCommand.ExitFailure, exit);
            Assert.Equal(string.Empty, text.ToString());
            Assert.Contains(
                "LUNATE_HOME points inside the workspace",
                errors.ToString(),
                StringComparison.Ordinal
            );
            return;
        }

        using var feed = new PackageFeedFixture();
        feed.Bundle();
        string inside = Path.Combine(feed.Worktree, "state");

        (int exitCode, string output) = await TestChildProcess.RunAsync(
            typeof(InstallCommandTests),
            Method,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["LUNATE_HOME"] = inside,
                ["LUNATE_TEST_INSTALL_WORKTREE"] = feed.Worktree,
            }
        );

        Assert.True(
            exitCode == 0,
            $"the guarded install did not refuse (exit {exitCode}):{output}"
        );
        Assert.False(Directory.Exists(inside), "the install wrote under the refused store");
    }
}
