using System.Text.Json;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>lunate extensions [--json]</c> subcommand (design D3): text and JSON shapes, exit codes,
/// the register-only inspect composition (the factory runs but nothing starts, nothing prompts and
/// no trust is recorded), the empty and <c>--no-extensions</c> views and the usage error.
/// </summary>
public sealed class ExtensionsCommandTests
{
    [Fact]
    public void Text_lists_the_loaded_and_denied_extensions_in_state_order()
    {
        using var temp = new TempDirectory();
        (string worktree, string store) = StoreWithHelloAndDeniedProject(temp);
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["extensions"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            workingDirectory: worktree
        );

        Assert.Equal(ExtensionsCommand.ExitOk, exitCode);
        Assert.Empty(errors.ToString());
        string[] lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // The two store extensions plus the builtin, which composes into every listing.
        Assert.Equal(3, lines.Length);
        Assert.StartsWith(
            "hello-project 0.1.0 (project) trust-denied, tools 0, commands 0, hooks 0, services 0",
            lines[0],
            StringComparison.Ordinal
        );
        Assert.StartsWith("hello 0.1.0 (global) loaded in ", lines[1], StringComparison.Ordinal);
        // The marker's group gains ", slow" when the load crossed the threshold, which a slower
        // runner legitimately does — assert the marker, not the shape of the parentheses.
        Assert.Contains("CLI-measured", lines[1], StringComparison.Ordinal);
        Assert.Contains(
            "tools 0, commands 0, hooks 3 (0 declared), services 1 (0 declared)",
            lines[1],
            StringComparison.Ordinal
        );
        Assert.StartsWith("roslyn 1.0.0 (builtin) loaded in ", lines[2], StringComparison.Ordinal);
        Assert.Contains("inactive 'roslyn__diagnostics'", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void The_register_only_run_never_starts_services_or_hooks_or_records_trust()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        ToolFixture.Install(store);
        string marker = temp.File("load-marker.txt");
        HelloFixture.WriteSettings(store, "hello", marker);
        HelloFixture.Install(
            Path.Combine(worktree, ".lunate", "extensions", "hello-project"),
            id: "hello-project"
        );
        var log = new RecordingExtensionLog();
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["extensions"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store, Log = log },
            workingDirectory: worktree
        );

        Assert.Equal(ExtensionsCommand.ExitOk, exitCode);
        Assert.True(
            File.Exists(marker),
            "the factory must run and register (the load happened); log:\n"
                + string.Join("\n", log.Messages)
        );
        Assert.True(log.Contains("created hello"));
        Assert.False(log.Contains("hello service started"), "no background service may start");
        Assert.False(log.Contains("hello service stopped"), "no background service may stop");
        Assert.False(log.Contains("hello session started"), "no session-start hook may run");
        Assert.False(log.Contains("hello session ended"), "no session-ending hook may run");
        Assert.False(
            File.Exists(Path.Combine(store, "trust.json")),
            "the non-interactive default must deny without recording trust"
        );
        Assert.Contains(
            "hello-project 0.1.0 (project) trust-denied",
            output.ToString(),
            StringComparison.Ordinal
        );
        Assert.Contains("CLI-measured", output.ToString(), StringComparison.Ordinal);
        string tools = Assert.Single(
            output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.StartsWith("tools ", StringComparison.Ordinal)
        );
        Assert.Contains(" loaded in ", tools, StringComparison.Ordinal);
        Assert.Contains(
            ", tools 1, commands 0, hooks 0, services 0",
            tools,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void A_failed_extension_reads_failed_and_exits_non_zero()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        BrokenFixture.Install(store);
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["extensions"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            workingDirectory: worktree
        );

        Assert.Equal(ExtensionsCommand.ExitFailure, exitCode);
        // Windows text output ends lines with \r\n; normalize at the test seam.
        string text = output.ToString().ReplaceLineEndings("\n");
        string failed = Assert.Single(
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.StartsWith("broken ", StringComparison.Ordinal)
        );
        Assert.Contains(
            "failed: extension 'broken': entry assembly 'Broken.dll' was not found in '",
            failed,
            StringComparison.Ordinal
        );
        Assert.EndsWith(
            "'., tools 0, commands 0, hooks 0, services 0",
            failed,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("see the log", failed, StringComparison.Ordinal);
        Assert.Contains("hello 0.1.0 (global) loaded in ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_listing_reports_an_inactive_tool_with_its_condition_and_reason()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        ToolFixture.Install(
            store,
            toolActivation: """{"echo":{"when":"workspace-root-has-file","patterns":["*.md"]}}"""
        );
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["extensions"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            workingDirectory: worktree
        );

        Assert.Equal(ExtensionsCommand.ExitOk, exitCode);
        string line = Assert.Single(
            output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries),
            candidate => candidate.StartsWith("tools ", StringComparison.Ordinal)
        );
        Assert.Contains(
            ", tools 1, inactive 'tools__echo' (workspace-root-has-file): no file matching '*.md' at the workspace root, commands 0, hooks 0, services 0",
            line,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void A_true_rule_leaves_the_count_row_untouched_at_the_listing_root()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        ToolFixture.Install(
            store,
            toolActivation: """{"echo":{"when":"workspace-root-has-file","patterns":["*.md"]}}"""
        );
        File.WriteAllText(Path.Combine(worktree, "marker.md"), "marker");
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["extensions"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            workingDirectory: worktree
        );

        Assert.Equal(ExtensionsCommand.ExitOk, exitCode);
        string line = Assert.Single(
            output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries),
            candidate => candidate.StartsWith("tools ", StringComparison.Ordinal)
        );
        Assert.Contains(
            ", tools 1, commands 0, hooks 0, services 0",
            line,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("inactive", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_names_the_inactive_tools_of_the_listing_root()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        ToolFixture.Install(
            store,
            toolActivation: """{"echo":{"when":"workspace-root-has-file","patterns":["*.md"]}}"""
        );
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["extensions", "--json"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            workingDirectory: worktree
        );

        Assert.Equal(ExtensionsCommand.ExitOk, exitCode);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        // The builtin composes into every listing; this test is about the store extension's row.
        JsonElement node = Assert.Single(
            document.RootElement.EnumerateArray(),
            entry => entry.GetProperty("id").GetString() == "tools"
        );
        JsonElement inactive = node.GetProperty("inactive");
        JsonElement entry = Assert.Single(inactive.EnumerateArray());
        Assert.Equal("tools__echo", entry.GetProperty("tool").GetString());
        Assert.Equal("workspace-root-has-file", entry.GetProperty("condition").GetString());
        Assert.Equal(
            "no file matching '*.md' at the workspace root",
            entry.GetProperty("reason").GetString()
        );
        Assert.Equal(1, node.GetProperty("tools").GetInt32());
    }

    [Fact]
    public void Json_lists_the_same_fields_as_an_array()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["extensions", "--json"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            workingDirectory: worktree
        );

        Assert.Equal(ExtensionsCommand.ExitOk, exitCode);
        Assert.Empty(errors.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        // The builtin composes into every listing; this test is about the store extension's row.
        JsonElement node = Assert.Single(
            document.RootElement.EnumerateArray(),
            entry => entry.GetProperty("id").GetString() == "hello"
        );
        Assert.Equal("hello", node.GetProperty("id").GetString());
        Assert.Equal("0.1.0", node.GetProperty("version").GetString());
        Assert.Equal("global", node.GetProperty("scope").GetString());
        Assert.Equal("loaded", node.GetProperty("state").GetString());
        Assert.True(node.GetProperty("loadMs").GetInt64() >= 0);
        // "slow" marks a load above the 500 ms threshold; a hot CI runner can cross it for
        // the tiny fixture, so the contract is the marker matching the measured load.
        bool slow = node.TryGetProperty("slow", out _);
        Assert.Equal(node.GetProperty("loadMs").GetInt64() >= 500, slow);
        Assert.Equal(0, node.GetProperty("tools").GetInt32());
        Assert.Equal(0, node.GetProperty("commands").GetInt32());
        Assert.Equal(3, node.GetProperty("hooks").GetInt32());
        Assert.Equal(1, node.GetProperty("services").GetInt32());
        // Register-only inspect mode dispatches nothing, so no hook timings exist.
        Assert.False(node.TryGetProperty("hookTimings", out _));
    }

    [Fact]
    public void The_empty_store_lists_only_the_inactive_roslyn_builtin()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        var seams = new ExtensionSeams
        {
            StorePath = temp.File("store"),
            Log = new RecordingExtensionLog(),
        };
        using var text = new StringWriter();
        using var json = new StringWriter();

        int textExit = Cli.Run(
            ["extensions"],
            text,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams,
            workingDirectory: worktree
        );
        int jsonExit = Cli.Run(
            ["extensions", "--json"],
            json,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams,
            workingDirectory: worktree
        );

        Assert.Equal(ExtensionsCommand.ExitOk, textExit);
        string textLine = Trimmed(text);
        // An empty store is not an empty listing: the builtin composes, and its tools are inactive
        // at a root with no project file.
        Assert.StartsWith("roslyn 1.0.0 (builtin) loaded in ", textLine, StringComparison.Ordinal);
        Assert.Contains(
            "tools 5, inactive 'roslyn__diagnostics'",
            textLine,
            StringComparison.Ordinal
        );
        Assert.Equal(ExtensionsCommand.ExitOk, jsonExit);
        using JsonDocument document = JsonDocument.Parse(json.ToString());
        JsonElement node = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("roslyn", node.GetProperty("id").GetString());
        Assert.Equal("builtin", node.GetProperty("scope").GetString());
        Assert.Equal(5, node.GetProperty("tools").GetInt32());
        Assert.Equal(5, node.GetProperty("inactive").GetArrayLength());
    }

    [Fact]
    public void The_no_extensions_flag_reads_disabled_and_never_reads_the_store()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        string marker = temp.File("load-marker.txt");
        HelloFixture.WriteSettings(store, "hello", marker);
        var seams = new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() };
        using var text = new StringWriter();
        using var json = new StringWriter();

        int textExit = Cli.Run(
            ["--no-extensions", "extensions"],
            text,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams
        );
        int jsonExit = Cli.Run(
            ["--no-extensions", "extensions", "--json"],
            json,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams
        );

        Assert.Equal(ExtensionsCommand.ExitOk, textExit);
        Assert.Equal("extensions disabled (--no-extensions)", Trimmed(text));
        Assert.Equal(ExtensionsCommand.ExitOk, jsonExit);
        Assert.Equal("[]", Trimmed(json));
        Assert.False(File.Exists(marker), "--no-extensions must not read the extension store");
    }

    private static string Trimmed(StringWriter writer) => writer.ToString().TrimEnd('\r', '\n');

    [Fact]
    public void An_unknown_flag_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["extensions", "--nope"],
            output,
            errors,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ExtensionsCommand.ExitUsage, exitCode);
        Assert.Contains(ExtensionsCommand.Usage, errors.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    private static (string Worktree, string Store) StoreWithHelloAndDeniedProject(
        TempDirectory temp
    )
    {
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        HelloFixture.Install(
            Path.Combine(worktree, ".lunate", "extensions", "hello-project"),
            id: "hello-project"
        );
        return (worktree, store);
    }
}
