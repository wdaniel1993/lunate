using System.Text.Json;
using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>The <c>Cli.Run</c> dispatch of the trust subcommand: seams, flags, exit codes, help.</summary>
public sealed class TrustCliTests
{
    [Fact]
    public void Trust_dispatches_with_the_working_directory_and_store_seams()
    {
        using var temp = new TempDirectory();
        (string worktree, string store) = ConfigOnly(temp);
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["trust", "--json"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store },
            workingDirectory: worktree
        );

        Assert.Equal(TrustCommand.ExitPending, exitCode);
        Assert.Empty(errors.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal(
            ["mcp.json", "mcpServer"],
            document
                .RootElement.GetProperty("pending")
                .EnumerateArray()
                .Select(node => node.GetProperty("kind").GetString())
        );
    }

    [Fact]
    public void Trust_yes_records_and_a_later_inspect_finds_nothing_pending()
    {
        using var temp = new TempDirectory();
        (string worktree, string store) = ConfigOnly(temp);
        var seams = new ExtensionSeams { StorePath = store };

        int approved = Cli.Run(
            ["trust", "--yes"],
            TextWriter.Null,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams,
            workingDirectory: worktree
        );
        int inspected = Cli.Run(
            ["trust"],
            new StringWriter(),
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: seams,
            workingDirectory: worktree
        );

        Assert.Equal(TrustCommand.ExitOk, approved);
        Assert.Equal(TrustCommand.ExitOk, inspected);
    }

    [Fact]
    public void Trust_with_no_extensions_records_the_project_config_and_its_servers()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        HelloFixture.Install(Path.Combine(worktree, ".lunate", "extensions", "hello"));
        string config = McpComposition.ProjectConfigPath(worktree);
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, """{ "mcpServers": { "srv": { "command": "x" } } }""");
        worktree = Workspace.Canonicalize(worktree);
        string store = temp.File("store");

        int exitCode = Cli.Run(
            ["trust", "--yes", "--no-extensions"],
            TextWriter.Null,
            TextWriter.Null,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store },
            workingDirectory: worktree
        );

        Assert.Equal(TrustCommand.ExitOk, exitCode);
        IReadOnlyList<TrustRecord> records = new ExtensionTrustStore(
            new ExtensionHostOptions { StorePath = store }
        ).Records(new Workspace(worktree).GitCommonDir ?? worktree);
        Assert.Equal(
            [TrustSection.McpConfig, TrustSection.McpServer],
            records.Select(record => record.Section)
        );
    }

    [Fact]
    public void Trust_usage_errors_go_to_stderr_with_exit_two()
    {
        using var temp = new TempDirectory();
        (string worktree, string store) = ConfigOnly(temp);
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["trust", "--yes", "--clear"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store },
            workingDirectory: worktree
        );

        Assert.Equal(TrustCommand.ExitUsage, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Usage: lunate trust", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Help_documents_the_trust_command()
    {
        using var writer = new StringWriter();

        int exitCode = Cli.Run(
            ["--help"],
            writer,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, exitCode);
        string help = writer.ToString();
        Assert.Contains(
            "lunate trust [--yes [--digest <sha>]] [--json] [--clear]",
            help,
            StringComparison.Ordinal
        );
        Assert.Contains("--digest", help, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--clear</c> is the whole repository's decision, single-file extensions included: their
    /// record is removed here, and the next load of the same bytes has nothing to stand on.
    /// </summary>
    [Fact]
    public void Trust_clear_removes_a_single_file_record_and_the_next_load_asks()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        string extensions = Path.Combine(worktree, ".lunate", "extensions");
        Directory.CreateDirectory(extensions);
        string path = Path.Combine(extensions, "acme.cs");
        File.WriteAllText(path, "#:extension acme-tools@1.0.0\n#:tool fetch_issue\n");
        string store = temp.File("store");
        var options = new ExtensionHostOptions { StorePath = store };
        string digest = SingleFileExtensionDigest.ComputeUnrestored(
            "acme-tools",
            [],
            File.ReadAllBytes(path)
        );
        var trust = new ExtensionTrustStore(options);
        trust.RecordSingleFileTrust(path, digest);
        Assert.Equal(SingleFileExtensionTrust.Trusted, trust.EvaluateSingleFileTrust(path, digest));

        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exitCode = Cli.Run(
            ["trust", "--clear"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store },
            workingDirectory: worktree
        );

        Assert.Equal(TrustCommand.ExitOk, exitCode);
        Assert.Empty(errors.ToString());
        Assert.Contains(
            "trust: cleared single-file extension " + path,
            output.ToString(),
            StringComparison.Ordinal
        );
        Assert.Equal(
            SingleFileExtensionTrust.Untrusted,
            trust.EvaluateSingleFileTrust(path, digest)
        );

        // The next load asks: discovery finds the same bytes and reports the missing decision.
        var loader = new ExtensionLoader(options);
        loader.Discover(worktree, Path.Combine(worktree, ".git"));
        SingleFileExtension row = Assert.Single(loader.SingleFileExtensions);
        Assert.Equal(SingleFileExtensionState.NotBuilt, row.State);
        Assert.Equal("not trusted yet; /extensions reload asks before it builds", row.Reason);
        Assert.Null(row.BuildKey);
    }

    private static (string Worktree, string Store) ConfigOnly(TempDirectory temp)
    {
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        string config = McpComposition.ProjectConfigPath(worktree);
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, """{ "mcpServers": { "srv": { "command": "x" } } }""");
        return (Workspace.Canonicalize(worktree), temp.File("store"));
    }
}
