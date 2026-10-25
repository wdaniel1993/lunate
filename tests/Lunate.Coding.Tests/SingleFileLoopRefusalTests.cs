using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The refusal half of the loop, in a composed session: a file whose bytes nobody approved cannot
/// be called and the listing says why, and a reload of a file the model just edited ends at the
/// trust question with the build that was already answering still loaded. Trust is the user's, so
/// every test here answers the question with a key and asserts that nothing crossed the line
/// before the answer.
/// </summary>
[Collection("mcp-processes")]
public sealed class SingleFileLoopRefusalTests
{
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
        SingleFileLoopSession.AutoEdit(host);
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
            SingleFileLoopSession.CompileWait
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
            SingleFileLoopSession.CompileWait
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
            SingleFileLoopSession.CompileWait
        );
        host.Console.SendText("n");
        await host.WaitForFrameAsync(
            "extension 'acme-tools' was not reloaded: the user did not trust 'acme-tools'",
            SingleFileLoopSession.CompileWait
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
    /// The model never approves trust, and this is that rule as a run: a file that is loaded and
    /// answering is edited by the model itself, the reload is asked for, and the changed bytes stop
    /// at the question. Nothing is compiled while the question waits (no compiler factory is even
    /// built), a declined question leaves the locks and the pointer exactly as they were, and the
    /// tool keeps answering from the build that was already loaded.
    /// </summary>
    [Fact]
    public async Task A_reload_of_the_file_the_model_just_edited_ends_at_the_trust_prompt()
    {
        using SingleFileLoopFixture fixture = SingleFileLoopFixture.Create();
        await fixture.BuildAsync(SingleFileLoopFixture.Source("\"first\""));
        string loadedKey = fixture.BuildCache().Current(SingleFileLoopFixture.Id)!.Key;
        string edited = SingleFileLoopFixture.Source("\"second\"");
        string path = Path.Combine(".lunate", "extensions", "acme.cs");
        using var host = new InteractiveSessionHost(
            extensions: fixture.Seams(),
            workingDirectory: fixture.Worktree
        );
        SingleFileLoopSession.AutoEdit(host);
        host.Console.Size = new ConsoleSize(600, 24);
        // 1: the loaded build answers. 2: the model edits the file. 3: after the refusal, the same
        // old build still answers.
        host.Client.Enqueue(
            Scripts.Call("call-1", "acme-tools__whoami", Scripts.Args()),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("answered"), Scripts.Stop());
        host.Client.Enqueue(
            Scripts.Call("call-2", "write", Scripts.Args(("path", path), ("content", edited))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("edited"), Scripts.Stop());
        host.Client.Enqueue(
            Scripts.Call("call-3", "acme-tools__whoami", Scripts.Args()),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("still first"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // The file was built before the session, so loading it asks nothing and compiles nothing.
        host.Console.SendText("/extensions reload acme-tools");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "extension 'acme-tools' reloaded — build ",
            SingleFileLoopSession.CompileWait
        );
        Assert.Equal(0, fixture.Compiles);
        Assert.Equal(0, fixture.Restores);

        host.Console.SendText("use it");
        host.Console.SendEnter();
        await host.WaitUntilAsync(
            () => host.Events.OfType<ToolCallResult>().Any(result => result.CallId == "call-1"),
            SingleFileLoopSession.CompileWait
        );
        Assert.Equal(
            "hello from the package: first",
            Assert
                .Single(host.Events.OfType<ToolCallResult>(), result => result.CallId == "call-1")
                .Output
        );

        // The model edits the file it just used.
        host.Console.SendText("change it");
        host.Console.SendEnter();
        await host.WaitUntilAsync(
            () => host.Events.OfType<ToolCallResult>().Any(result => result.CallId == "call-2"),
            SingleFileLoopSession.CompileWait
        );
        Assert.Equal(edited, File.ReadAllText(fixture.SourcePath));

        // The reload stops at the question. While it waits, the changed file has reached no
        // compiler and no restore engine — the factory is only asked after trust answers.
        host.Console.SendText("/extensions reload acme-tools");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "Trust the single-file extension 'acme-tools' 1.0.0",
            SingleFileLoopSession.CompileWait
        );
        Assert.Contains(
            "It changed since it was trusted last",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Equal(0, fixture.Compiles);
        Assert.Equal(0, fixture.Restores);

        host.Console.SendText("n");
        await host.WaitForFrameAsync(
            "extension 'acme-tools' was not reloaded: the user did not trust 'acme-tools'",
            SingleFileLoopSession.CompileWait
        );
        Assert.Equal(0, fixture.Compiles);
        Assert.Equal(0, fixture.Restores);
        // Nothing was written for the changed bytes: the pointer still names the loaded build.
        Assert.Equal(loadedKey, fixture.BuildCache().Current(SingleFileLoopFixture.Id)!.Key);

        // The old build is still what answers, and the row says why nothing newer is live.
        host.Console.SendText("use it again");
        host.Console.SendEnter();
        await host.WaitUntilAsync(
            () => host.Events.OfType<ToolCallResult>().Any(result => result.CallId == "call-3"),
            SingleFileLoopSession.CompileWait
        );
        ToolCallResult answer = Assert.Single(
            host.Events.OfType<ToolCallResult>(),
            result => result.CallId == "call-3"
        );
        Assert.False(answer.IsError, answer.Output);
        Assert.Equal("hello from the package: first", answer.Output);

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await host.WaitForFrameAsync(
            "acme-tools 1.0.0 (project, single file) stale — the file, its declared packages or the resolution in its lock changed since it was trusted",
            SingleFileLoopSession.CompileWait
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}
