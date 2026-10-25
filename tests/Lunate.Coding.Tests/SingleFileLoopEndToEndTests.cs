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
    /// The refusal side of the same loop: a file whose bytes nobody approved cannot be called, and
    /// the listing says that trust is what stands in the way rather than leaving the reader to
    /// guess why a discovered file provides nothing. Declining the question changes nothing:
    /// nothing is restored, nothing is compiled, and the tool is still absent afterwards.
    /// </summary>
    [Fact]
    public async Task An_untrusted_files_tool_is_not_callable_and_the_listing_says_why()
    {
        using SingleFileLoopFixture fixture = SingleFileLoopFixture.Create();
        // The file is there; no decision was ever taken for these bytes.
        fixture.Write(SingleFileLoopFixture.Source("\"never\""));
        using var host = new InteractiveSessionHost(
            extensions: fixture.Seams(),
            workingDirectory: fixture.Worktree
        );
        AutoEdit(host);
        host.Console.Size = new ConsoleSize(600, 24);
        host.Client.Enqueue(
            Scripts.Call("call-1", "acme-tools__whoami", Scripts.Args()),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("no tool"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("still no tool"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // The row is `not-built` and says why nothing is live: an untrusted file is not a broken
        // one, and the sentence names the command that asks.
        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "acme-tools 1.0.0 (project, single file) not-built, tools 1, commands 0, hooks 0, services 0",
            CompileWait
        );
        Assert.Contains(
            "not trusted yet; /extensions reload asks before it builds",
            host.LastFrame,
            StringComparison.Ordinal
        );

        // The model sees no such tool — and a call it invents anyway is refused by name, with the
        // list it could have chosen from.
        host.Console.SendText("use the tool");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        Assert.DoesNotContain(
            host.Client.RequestOptions[0]!.Tools!.OfType<AIFunction>(),
            tool => tool.Name == "acme-tools__whoami"
        );
        await host.WaitUntilAsync(
            () => host.Events.OfType<ToolCallResult>().Any(result => result.CallId == "call-1"),
            CompileWait
        );
        ToolCallResult refusal = Assert.Single(
            host.Events.OfType<ToolCallResult>(),
            result => result.CallId == "call-1"
        );
        Assert.True(refusal.IsError);
        Assert.Contains(
            "Unknown tool 'acme-tools__whoami'.",
            refusal.Output,
            StringComparison.Ordinal
        );

        // The user declines the question the reload asks: nothing is restored, nothing is compiled,
        // and the answer is reported with the reason the preflight stopped.
        host.Console.SendText("/extensions reload acme-tools");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "Trust the single-file extension 'acme-tools' 1.0.0",
            CompileWait
        );
        host.Console.SendText("n");
        await host.WaitForFrameAsync(
            "extension 'acme-tools' was not reloaded: the user did not trust 'acme-tools'",
            CompileWait
        );
        Assert.Equal(0, fixture.Restores);
        Assert.Equal(0, fixture.Compiles);

        // And the next request still cannot see the tool: a refusal is not a partial load.
        host.Console.SendText("try again");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(3);
        Assert.DoesNotContain(
            host.Client.RequestOptions[2]!.Tools!.OfType<AIFunction>(),
            tool => tool.Name == "acme-tools__whoami"
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    /// <summary>
    /// The same loop as a recorded run: <see cref="ScriptedChatClient"/> drives the model, the
    /// session's model is a built-in catalog id (so no provider is ever constructed), and the
    /// assertion is the run's own record — the tool calls in order, each with its result, and the
    /// model the factory was asked for after the harness was rebuilt. The suite runs this class
    /// with <c>HOME</c> pointing at an empty directory: the fixture store, the packages folder and
    /// the build are all under the temp root, which is what makes the numbers above reproducible.
    /// </summary>
    [Fact]
    public async Task A_recorded_run_carries_the_write_the_trusted_reload_and_the_call_in_order()
    {
        using SingleFileLoopFixture fixture = SingleFileLoopFixture.Create();
        string path = Path.Combine(".lunate", "extensions", "acme.cs");
        string source = SingleFileLoopFixture.Source("\"recorded\"");
        var scripted = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "write", Scripts.Args(("path", path), ("content", source))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("written"), Scripts.Stop())
            .Enqueue(
                Scripts.Call("call-2", "acme-tools__whoami", Scripts.Args()),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        var factory = new FakeChatClientFactory(scripted);
        using var host = new InteractiveSessionHost(
            factory: factory,
            extensions: fixture.Seams(),
            workingDirectory: fixture.Worktree,
            launch: new LaunchOptions { Model = "gpt-4o-mini" }
        );
        AutoEdit(host);
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // A catalog id, resolved by the session, and a scripted client built for it: the run never
        // touches a provider.
        Assert.Equal("gpt-4o-mini", factory.CreatedModel?.Id);

        await WriteAndApproveReloadAsync(host, fixture, source);
        Assert.Equal("gpt-4o-mini", factory.CreatedModel?.Id);

        host.Console.SendText("use it");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => scripted.Requests.Count == 4, CompileWait);

        // The record: what ran, in the order the session ran it.
        List<string> record = [];
        foreach (AgentEvent recorded in host.Events)
        {
            switch (recorded)
            {
                case ToolCallStart start:
                    record.Add("call " + start.ToolName);
                    break;
                case ToolCallResult result:
                    record.Add((result.IsError ? "error " : "ok ") + result.CallId);
                    break;
            }
        }

        Assert.Equal(["call write", "ok call-1", "call acme-tools__whoami", "ok call-2"], record);
        Assert.Equal(
            "hello from the package: recorded",
            Assert
                .Single(host.Events.OfType<ToolCallResult>(), result => result.CallId == "call-2")
                .Output
        );

        // Everything the run wrote is under the temp root it was given: the trust decision, the
        // lock and the build, and the file the model wrote in the worktree.
        Assert.True(File.Exists(Path.Combine(fixture.Store, "trust.json")));
        string buildRoot = Path.Combine(
            fixture.Store,
            "cache",
            "extensions",
            SingleFileLoopFixture.Id
        );
        Assert.NotEmpty(
            Directory.GetFiles(
                Path.Combine(buildRoot, "builds"),
                "*.dll",
                SearchOption.AllDirectories
            )
        );
        Assert.True(File.Exists(fixture.SourcePath));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    /// <summary>
    /// Turn one and the user's answer: the model writes the file, the user asks for the reload, the
    /// question is approved, and the reload reports the build it loaded.
    /// </summary>
    private static async Task WriteAndApproveReloadAsync(
        InteractiveSessionHost host,
        SingleFileLoopFixture fixture,
        string source
    )
    {
        host.Console.SendText("write the extension");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => File.Exists(fixture.SourcePath), CompileWait);
        Assert.Equal(source, File.ReadAllText(fixture.SourcePath));

        host.Console.SendText("/extensions reload acme-tools");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "Trust the single-file extension 'acme-tools' 1.0.0",
            CompileWait
        );
        host.Console.SendText("y");
        await host.WaitForFrameAsync("extension 'acme-tools' reloaded — build ", CompileWait);
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
