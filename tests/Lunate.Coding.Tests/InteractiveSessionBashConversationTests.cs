using System.Text.Json;
using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionBashConversationTests
{
    [Fact]
    public async Task A_normal_bash_line_adds_the_command_and_result_to_the_next_request()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await RunBashLineAsync(host, "!echo conversation");
        await host.WaitUntilAsync(() =>
            !host.Session.IsBashRunning
            && host.ScrollbackText.Contains("exit 0", StringComparison.Ordinal)
        );

        host.Console.SendText("next");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        ChatMessage report = Assert.Single(
            host.Client.Requests[0],
            message =>
                message.Role == ChatRole.User
                && Text(message).Contains("echo conversation", StringComparison.Ordinal)
        );
        Assert.Contains("conversation", Text(report), StringComparison.Ordinal);
        Assert.Contains(
            host.Client.Requests[0],
            message =>
                message.Role == ChatRole.User
                && Text(message).Contains("next", StringComparison.Ordinal)
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_silent_form_leaves_the_next_request_byte_identical()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first answer"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("first");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        string baseline = JsonSerializer.Serialize(host.Client.Requests[0], SessionJson.Options);
        int baselineCount = host.Client.Requests[0].Count;

        await RunBashLineAsync(host, "!!echo secret");
        await host.WaitUntilAsync(() =>
            !host.Session.IsBashRunning
            && host.ScrollbackText.Contains("echo secret", StringComparison.Ordinal)
        );

        host.Client.Enqueue(Scripts.Text("second answer"), Scripts.Stop());
        host.Console.SendText("second");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);

        IReadOnlyList<ChatMessage> messages = host.Client.Requests[1];
        Assert.Equal(baselineCount + 2, messages.Count);
        Assert.Equal(
            baseline,
            JsonSerializer.Serialize(messages.Take(baselineCount), SessionJson.Options)
        );
        Assert.DoesNotContain(
            messages,
            message => Text(message).Contains("secret", StringComparison.Ordinal)
        );
        Assert.Contains(
            "\u2713 bash  echo secret \u00b7 exit 0",
            host.ScrollbackText,
            StringComparison.Ordinal
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_bash_submission_while_a_turn_runs_is_refused_with_a_notice()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("streamed"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        host.Console.SendText("!ls");
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("a turn is running", StringComparison.Ordinal);
        });

        Assert.Null(host.Session.PendingApproval);
        Assert.False(host.Session.IsBashRunning);
        Assert.Single(host.Client.Requests);
        Assert.DoesNotContain(host.Events, agentEvent => agentEvent is ToolCallStart);

        host.Client.Release(1);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_prompt_while_a_bash_command_runs_is_refused_with_a_notice()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!" + LongRunningCommand());
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendText("y");
        await host.WaitUntilAsync(() =>
            host.Session.PendingApproval is null && host.Session.IsBashRunning
        );

        host.Console.SendText("hello");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("a bash command is running", StringComparison.Ordinal);
        });

        Assert.Empty(host.Client.Requests);

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsBashRunning);
        host.Console.Complete();
        await run;
    }

    private static async Task RunBashLineAsync(InteractiveSessionHost host, string line)
    {
        host.Console.SendText(line);
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendText("y");
    }

    private static string LongRunningCommand() =>
        OperatingSystem.IsWindows()
            ? "powershell.exe -NoProfile -Command \"Start-Sleep 300\""
            : "sleep 300";

    private static string Text(ChatMessage message) =>
        string.Join(" ", message.Contents.OfType<TextContent>().Select(content => content.Text));
}
