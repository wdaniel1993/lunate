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
        Assert.Single(document.RootElement.GetProperty("pending").EnumerateArray());
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
