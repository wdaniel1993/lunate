using Lunate.Agent;
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
        SingleFileLoopSession.AutoEdit(host);
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
        await host.WaitUntilAsync(
            () => File.Exists(fixture.SourcePath),
            SingleFileLoopSession.CompileWait
        );
        Assert.Equal(
            source.ReplaceLineEndings(Environment.NewLine),
            File.ReadAllText(fixture.SourcePath)
        );

        // The file exists now, after composition: the listing must read the state, not the
        // snapshot discovery took at session start.
        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "acme-tools 1.0.0 (project, single file) not-built",
            SingleFileLoopSession.CompileWait
        );

        // The user asks for it to go live, and the question is the user's: the prompt names the
        // file and the declared package before anything is restored or compiled.
        host.Console.SendText("/extensions reload acme-tools");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "Trust the single-file extension 'acme-tools' 1.0.0",
            SingleFileLoopSession.CompileWait
        );
        Assert.Contains(fixture.SourcePath, host.LastFrame, StringComparison.Ordinal);
        Assert.Contains(
            "It declares Acme.Tools@2.0.0 and their dependencies.",
            host.LastFrame,
            StringComparison.Ordinal
        );

        host.Console.SendText("y");
        await host.WaitForFrameAsync(
            "extension 'acme-tools' reloaded — build ",
            SingleFileLoopSession.CompileWait
        );
        Assert.Null(host.Session.PendingTrustPrompt);

        // The row now names the build the reload loaded, not the state discovery saw.
        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "acme-tools 1.0.0 (project, single file) built",
            SingleFileLoopSession.CompileWait
        );
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
            SingleFileLoopSession.CompileWait
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
        SingleFileLoopSession.AutoEdit(host);
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // A catalog id, resolved by the session, and a scripted client built for it: the run never
        // touches a provider.
        Assert.Equal("gpt-4o-mini", factory.CreatedModel?.Id);

        await SingleFileLoopSession.WriteAndApproveReloadAsync(host, fixture, source);
        Assert.Equal("gpt-4o-mini", factory.CreatedModel?.Id);

        host.Console.SendText("use it");
        host.Console.SendEnter();
        // Wait on the record itself, not the model's request count: the session appends events on
        // its own pump thread, so the count can run ahead of the events this test about to read.
        await host.WaitUntilAsync(
            () => host.Events.OfType<ToolCallResult>().Count() == 2,
            SingleFileLoopSession.CompileWait
        );

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
}
