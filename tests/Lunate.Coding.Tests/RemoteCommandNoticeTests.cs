using System.Text.Json.Nodes;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// A remote manifest command through the real composition: a node child speaking the wire, loaded
/// by <see cref="ExtensionSession"/>, its declared commands composed into the one registry. The
/// reply reaches the registry as the command's notice naming its source-qualified id; a failing
/// handler is the same error-notice shape an in-process handler produces; and a child without the
/// capability is offered nothing, which is what <c>/extensions</c> and dispatch then report.
/// </summary>
public sealed class RemoteCommandNoticeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_remote_command_output_reaches_the_registry_naming_its_id()
    {
        using var temp = new TempDirectory();
        string worktree = CreateWorktree(temp);
        var log = new RecordingExtensionLog();
        await using ExtensionSession host = await ExtensionSession.StartAsync(
            new WorkspaceInfo(Workspace.Canonicalize(worktree), null, null),
            Install(temp, log, commands: ["ping", "explode"]),
            Ct
        );
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [.. host.CommandSources()],
            log,
            Ct
        );
        Assert.Contains(registry.Descriptors, descriptor => descriptor.Name == "ping");

        CommandResult result = await registry.InvokeAsync(
            new CommandInvocation("ping", "hello world"),
            Ct
        );

        Assert.False(result.IsError);
        Assert.Equal("ext:e2e:ping: pong hello world", result.Message);
    }

    [Fact]
    public async Task A_failing_remote_handler_matches_the_in_process_error_notice()
    {
        using var temp = new TempDirectory();
        string worktree = CreateWorktree(temp);
        var log = new RecordingExtensionLog();
        await using ExtensionSession host = await ExtensionSession.StartAsync(
            new WorkspaceInfo(Workspace.Canonicalize(worktree), null, null),
            Install(temp, log, commands: ["explode"]),
            Ct
        );
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [.. host.CommandSources()],
            log,
            Ct
        );

        CommandResult result = await registry.InvokeAsync(
            new CommandInvocation("explode", string.Empty),
            Ct
        );

        Assert.True(result.IsError);
        Assert.Equal(
            "command ext:e2e:explode failed: the extension broke while pinging — see /extensions for the extension's state",
            result.Message
        );
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("ext:e2e:explode", StringComparison.Ordinal)
                && message.Contains("the extension broke while pinging", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_child_without_the_capability_is_absent_from_the_listing_and_reported()
    {
        using var temp = new TempDirectory();
        string worktree = CreateWorktree(temp);
        var log = new RecordingExtensionLog();
        await using ExtensionSession host = await ExtensionSession.StartAsync(
            new WorkspaceInfo(Workspace.Canonicalize(worktree), null, null),
            Install(temp, log, commands: ["ghost"], protocolVersion: 1, advertisesCommands: false),
            Ct
        );
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [.. host.CommandSources()],
            log,
            Ct
        );

        // Not in autocomplete and not dispatchable.
        Assert.DoesNotContain(registry.Descriptors, descriptor => descriptor.Name == "ghost");
        Assert.DoesNotContain(
            registry.CompletionItems,
            item => item.Text.Contains("ghost", StringComparison.Ordinal)
        );
        CommandResult result = await registry.InvokeAsync(
            new CommandInvocation("ghost", string.Empty),
            Ct
        );
        Assert.Equal("unknown command: /ghost", result.Message);

        // And the extensions view reports the declaration as not provided.
        // The builtin also composes; this test is about the remote extension's row.
        string segment = Assert.Single(
            ExtensionsView.Segments(
                host.Descriptors,
                host.LoadResults,
                id => host.RegisteredCounts(id, registry.Shadowed)
            ),
            candidate => candidate.StartsWith("e2e ", StringComparison.Ordinal)
        );
        Assert.Contains("not provided: ghost", segment, StringComparison.Ordinal);
    }

    private static string CreateWorktree(TempDirectory temp)
    {
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        return worktree;
    }

    /// <summary>
    /// Installs one remote extension under the caller's temp store: a node child answering
    /// <c>initialize</c>, <c>command/invoke</c> and <c>shutdown</c> over the real wire.
    /// </summary>
    private static ExtensionSeams Install(
        TempDirectory temp,
        RecordingExtensionLog log,
        IReadOnlyList<string> commands,
        int protocolVersion = 2,
        bool advertisesCommands = true
    )
    {
        string store = temp.File("store");
        string directory = Path.Combine(store, "extensions", "e2e");
        Directory.CreateDirectory(directory);
        var manifest = new JsonObject
        {
            ["id"] = "e2e",
            ["version"] = "0.1.0",
            ["apiVersion"] = "^1.0.0",
            ["runtime"] = new JsonObject
            {
                ["command"] = "node",
                ["args"] = new JsonArray("-e", Script(protocolVersion, advertisesCommands)),
            },
            ["commands"] = new JsonArray([.. commands.Select(name => JsonValue.Create(name))]),
        };
        File.WriteAllText(Path.Combine(directory, "extension.json"), manifest.ToJsonString());
        return new ExtensionSeams { StorePath = store, Log = log };
    }

    /// <summary>The child's answers: ping echoes, explode fails, anything else is unknown.</summary>
    private static string Script(int protocolVersion, bool advertisesCommands)
    {
        string commands = advertisesCommands ? $", commands: true" : string.Empty;
        return "const readline = require('readline');"
            + "const rl = readline.createInterface({ input: process.stdin });"
            + "function send(frame) { process.stdout.write(JSON.stringify(frame) + '\\n'); }"
            + "rl.on('line', line => {"
            + "  let frame;"
            + "  try { frame = JSON.parse(line); } catch (error) { return; }"
            + "  if (frame.method === 'initialize') {"
            + "    send({ jsonrpc: '2.0', id: frame.id, result: { protocolVersion: "
            + protocolVersion
            + ", hooks: [], services: []"
            + commands
            + " } });"
            + "  } else if (frame.method === 'command/invoke') {"
            + "    const name = (frame.params || {}).name;"
            + "    const argument = (frame.params || {}).argument || '';"
            + "    if (name === 'ping') {"
            + "      send({ jsonrpc: '2.0', id: frame.id, result: { output: 'pong ' + argument,"
            + " error: null, disposition: 'completed' } });"
            + "    } else if (name === 'explode') {"
            + "      send({ jsonrpc: '2.0', id: frame.id, result: { output: null,"
            + " error: 'the extension broke while pinging', disposition: 'failed' } });"
            + "    } else {"
            + "      send({ jsonrpc: '2.0', id: frame.id, result: { output: null,"
            + " error: 'unknown command ' + name, disposition: 'failed' } });"
            + "    }"
            + "  } else if (frame.method === 'shutdown') {"
            + "    send({ jsonrpc: '2.0', id: frame.id, result: null });"
            + "    setTimeout(() => process.exit(0), 10);"
            + "  }"
            + "});";
    }
}
