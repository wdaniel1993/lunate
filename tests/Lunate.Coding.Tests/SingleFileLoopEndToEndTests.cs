using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The phase-4 loop, through a composed session with the real compiler: the model writes one
/// <c>.cs</c> file declaring one package, the user trusts it at the reload, and the tool it
/// declares is callable in the same session — the model's next request declares it and the call
/// answers with code from the declared package. It shares the interactive tests' serial
/// collection, like the other suites that compose a full session with a console and a frame pump.
/// </summary>
[Collection("mcp-processes")]
public sealed class SingleFileLoopEndToEndTests
{
    /// <summary>
    /// The window for waits that depend on a compile: two Roslyn compiles against a reference
    /// pack run inside these tests, and a cold CI runner needs more than a frame's 5 s.
    /// </summary>
    private static readonly TimeSpan CompileWait = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task A_model_written_file_becomes_a_callable_tool_after_the_user_trusts_it()
    {
        using SingleFileLoopFixture fixture = SingleFileLoopFixture.Create();
        string path = Path.Combine(".lunate", "extensions", "acme.cs");
        string source = SingleFileLoopFixture.Source("\"ran\"");
        using var host = new InteractiveSessionHost(
            extensions: fixture.Seams(),
            workingDirectory: fixture.Worktree
        );
        AutoEdit(host);
        host.Console.Size = new ConsoleSize(600, 24);
        // Turn one: the model writes the file, then says so. Turn two (a separate request, after
        // the user's reload): it calls the tool the reload made live.
        host.Client.Enqueue(
            Scripts.Call("call-1", "write", Scripts.Args(("path", path), ("content", source))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("written"), Scripts.Stop());
        host.Client.Enqueue(
            Scripts.Call("call-2", "acme-tools__whoami", Scripts.Args()),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("write the extension");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => File.Exists(fixture.SourcePath), CompileWait);
        Assert.Equal(source, File.ReadAllText(fixture.SourcePath));

        // The file exists now, after composition: the listing must read the state, not the
        // snapshot discovery took at session start.
        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "acme-tools 1.0.0 (project, single file) not-built",
            CompileWait
        );

        // The user asks for it to go live, and the question is the user's: the prompt names the
        // file and the declared package before anything is restored or compiled.
        host.Console.SendText("/extensions reload acme-tools");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "Trust the single-file extension 'acme-tools' 1.0.0",
            CompileWait
        );
        Assert.Contains(fixture.SourcePath, host.LastFrame, StringComparison.Ordinal);
        Assert.Contains(
            "It declares Acme.Tools@2.0.0 and their dependencies.",
            host.LastFrame,
            StringComparison.Ordinal
        );

        host.Console.SendText("y");
        await host.WaitForFrameAsync("extension 'acme-tools' reloaded — build ", CompileWait);
        Assert.Null(host.Session.PendingTrustPrompt);

        // The row now names the build the reload loaded, not the state discovery saw.
        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("acme-tools 1.0.0 (project, single file) built", CompileWait);
        Assert.Contains(", resolved: Acme.Tools 2.0.0", host.LastFrame, StringComparison.Ordinal);

        // The model's next request carries the tool, and the answer is the package's code plus the
        // probe this build was compiled from — so the compile read the declared package.
        host.Console.SendText("use it");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(3);
        Assert.Single(
            host.Client.RequestOptions[2]!.Tools!.OfType<AIFunction>(),
            tool => tool.Name == "acme-tools__whoami"
        );

        await host.WaitUntilAsync(
            () => host.Events.OfType<ToolCallResult>().Any(result => result.CallId == "call-2"),
            CompileWait
        );
        ToolCallResult answer = Assert.Single(
            host.Events.OfType<ToolCallResult>(),
            result => result.CallId == "call-2"
        );
        Assert.False(answer.IsError, answer.Output);
        Assert.Equal("hello from the package: ran", answer.Output);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    /// <summary>
    /// The session's default approval policy asks before a write; these tests are about the
    /// extension loop, so the policy is the auto-edit one (writes inside the workspace run).
    /// </summary>
    private static void AutoEdit(InteractiveSessionHost host) =>
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "approval": "auto-edit"
            }
            """
        );
}
