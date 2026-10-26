using System.Text.Json;
using Lunate.Extensibility;
using Lunate.Extensibility.Restore;

namespace Lunate.Coding.Tests;

/// <summary>
/// <c>lunate uninstall</c> (spec: "Uninstall", "Install output and <c>--dry-run</c>"): the text and
/// JSON shapes, the files it deletes and the ones it leaves, the refusal on a changed file and the
/// <c>--force</c> that overrides it, the extension id it resolves, and the exit codes.
/// </summary>
public sealed class UninstallCommandTests
{
    [Fact]
    public void Uninstall_force_removes_a_record_nobody_can_read_and_names_what_it_cannot_know()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(id: "acme-tools", extensionId: "acme-tools", skills: ["greek"]);
        Assert.Equal(0, feed.Run(["acme-tools@1.4.2"], out _, out _));
        string record = Path.Combine(
            feed.Store,
            "packages",
            "acme-tools",
            PackageInstallRecordJson.FileName
        );
        File.WriteAllText(record, "{ this is not a record");

        int exit = feed.RunUninstall(
            ["acme-tools", "--force"],
            out string output,
            out string errors
        );

        Assert.Equal(UninstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains($"record: '{record}' removed", output, StringComparison.Ordinal);
        // Every path it removed is printed, not a count to take on trust.
        Assert.Contains(
            Path.Combine(feed.Store, "extensions", "acme-tools", "acme-tools.dll"),
            output,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"not known: '{Path.Combine(feed.Store, "skills")}'",
            output,
            StringComparison.Ordinal
        );
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "packages", "acme-tools")));
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "extensions", "acme-tools")));
        // A skill directory is exactly what only a readable record could have named.
        Assert.True(File.Exists(Path.Combine(feed.Store, "skills", "greek", "SKILL.md")));
    }

    [Fact]
    public void Uninstall_without_force_still_refuses_an_unreadable_record()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(id: "acme-tools", extensionId: "acme-tools", skills: ["greek"]);
        Assert.Equal(0, feed.Run(["acme-tools@1.4.2"], out _, out _));
        string record = Path.Combine(
            feed.Store,
            "packages",
            "acme-tools",
            PackageInstallRecordJson.FileName
        );
        File.WriteAllText(record, "{ this is not a record");

        int exit = feed.RunUninstall(["acme-tools"], out string output, out string errors);

        Assert.Equal(UninstallCommand.ExitFailure, exit);
        Assert.Empty(output);
        Assert.Contains(record, errors, StringComparison.Ordinal);
        Assert.Contains("nothing acts on it", errors, StringComparison.Ordinal);
        Assert.True(File.Exists(record));
        Assert.True(
            File.Exists(Path.Combine(feed.Store, "extensions", "acme-tools", "acme-tools.dll"))
        );
    }

    [Fact]
    public void Uninstall_prints_what_it_removed_and_keeps_the_nupkg_and_the_lock()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek", "roman"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));
        string nupkg = Path.Combine(
            feed.GlobalPackages,
            "acme.toolkit",
            "1.4.2",
            "acme.toolkit.1.4.2.nupkg"
        );
        string @lock = Path.Combine(
            feed.Store,
            "cache",
            "extensions",
            "acme-tools",
            ExtensionLock.FileName
        );

        int exit = feed.RunUninstall(["Acme.Toolkit"], out string output, out string errors);

        Assert.Equal(UninstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        string[] lines = output.TrimEnd('\n').Split('\n');
        Assert.Equal("package: Acme.Toolkit 1.4.2", lines[0]);
        Assert.Equal("scope: user", lines[1]);
        Assert.Equal($"removed: 4 files under '{feed.Store}'", lines[2]);
        Assert.Equal(
            $"record: '{Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json")}' removed",
            lines[3]
        );

        Assert.False(Directory.Exists(Path.Combine(feed.Store, "extensions", "acme-tools")));
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "skills", "greek")));
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "packages", "acme.toolkit")));
        Assert.True(File.Exists(nupkg), "the nupkg is NuGet's cache, not installed content");
        Assert.True(File.Exists(@lock), "the lock is the record of a resolution, not content");
    }

    [Fact]
    public void A_hand_added_file_is_left_and_named_in_the_output()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));
        string handAdded = Path.Combine(feed.Store, "extensions", "acme-tools", "notes.txt");
        File.WriteAllText(handAdded, "mine");

        int exit = feed.RunUninstall(["Acme.Toolkit"], out string output, out string errors);

        Assert.Equal(UninstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("is not empty", output, StringComparison.Ordinal);
        Assert.Contains(handAdded, output, StringComparison.Ordinal);
        Assert.Contains("the package did not write", output, StringComparison.Ordinal);
        Assert.True(File.Exists(handAdded));
    }

    [Fact]
    public void An_edited_file_refuses_without_force_and_exits_one()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));
        string assembly = Path.Combine(feed.Store, "extensions", "acme-tools", "acme-tools.dll");
        File.WriteAllText(assembly, "edited by hand");

        int exit = feed.RunUninstall(["Acme.Toolkit"], out string output, out string errors);

        Assert.Equal(UninstallCommand.ExitFailure, exit);
        Assert.Empty(output);
        Assert.Contains(assembly, errors, StringComparison.Ordinal);
        Assert.Contains("--force", errors, StringComparison.Ordinal);
        Assert.True(File.Exists(assembly));

        int forced = feed.RunUninstall(
            ["Acme.Toolkit", "--force"],
            out string forcedOutput,
            out string forcedErrors
        );

        Assert.Equal(UninstallCommand.ExitOk, forced);
        Assert.Empty(forcedErrors);
        Assert.Contains("forced: a changed file", forcedOutput, StringComparison.Ordinal);
        Assert.False(File.Exists(assembly));
    }

    [Fact]
    public void An_extension_id_names_the_one_package_that_provides_it()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));

        int exit = feed.RunUninstall(["acme-tools"], out string output, out string errors);

        Assert.Equal(UninstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("package: Acme.Toolkit 1.4.2", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "extensions", "acme-tools")));
    }

    [Fact]
    public void Json_names_what_changed()
    {
        using var feed = new PackageFeedFixture();
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out _));
        string handAdded = Path.Combine(feed.Store, "extensions", "acme-tools", "notes.txt");
        File.WriteAllText(handAdded, "mine");

        int exit = feed.RunUninstall(
            ["Acme.Toolkit", "--json"],
            out string output,
            out string errors
        );

        Assert.Equal(UninstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        Assert.Equal("Acme.Toolkit", root.GetProperty("id").GetString());
        Assert.Equal("1.4.2", root.GetProperty("version").GetString());
        Assert.Equal("user", root.GetProperty("scope").GetString());
        Assert.True(root.GetProperty("recordRemoved").GetBoolean());
        Assert.False(root.GetProperty("forced").GetBoolean());
        Assert.Equal(3, root.GetProperty("removed").GetArrayLength());
        Assert.Equal(
            [handAdded],
            [.. root.GetProperty("left").EnumerateArray().Select(item => item.GetString())]
        );
        Assert.Equal(
            [Path.Combine(feed.Store, "extensions", "acme-tools")],
            [.. root.GetProperty("directories").EnumerateArray().Select(item => item.GetString())]
        );
    }

    [Fact]
    public void An_unknown_package_fails_naming_the_store_and_writes_nothing_to_stdout()
    {
        using var feed = new PackageFeedFixture();

        int exit = feed.RunUninstall(["Acme.Toolkit"], out string output, out string errors);

        Assert.Equal(UninstallCommand.ExitFailure, exit);
        Assert.Empty(output);
        Assert.Contains("Acme.Toolkit", errors, StringComparison.Ordinal);
        Assert.Contains(feed.Store, errors, StringComparison.Ordinal);
    }

    [Fact]
    public void A_usage_error_exits_two()
    {
        using var feed = new PackageFeedFixture();

        foreach (
            string[] args in new[]
            {
                Array.Empty<string>(),
                ["Acme.Toolkit", "Other.Package"],
                ["Acme.Toolkit", "--nope"],
            }
        )
        {
            int exit = feed.RunUninstall(args, out string output, out string errors);

            Assert.Equal(UninstallCommand.ExitUsage, exit);
            Assert.Empty(output);
            Assert.Contains(UninstallCommand.Usage, errors, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_project_install_is_removed_from_its_worktree()
    {
        using var feed = new PackageFeedFixture();
        GitTestRepo.Init(feed.Worktree);
        feed.Bundle(skills: ["greek"]);
        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2", "--project"], out _, out _));

        int exit = feed.RunUninstall(["Acme.Toolkit"], out string output, out string errors);

        Assert.Equal(UninstallCommand.ExitOk, exit);
        Assert.Empty(errors);
        Assert.Contains("scope: project", output, StringComparison.Ordinal);
        Assert.Contains(
            $"worktree: {Workspace.Canonicalize(feed.Worktree)}",
            output,
            StringComparison.Ordinal
        );
        Assert.False(
            Directory.Exists(Path.Combine(feed.Worktree, ".lunate", "extensions", "acme-tools"))
        );
        Assert.False(Directory.Exists(Path.Combine(feed.Worktree, ".agents", "skills", "greek")));
        Assert.False(
            File.Exists(Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json"))
        );
    }
}
