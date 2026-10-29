using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// First run (design: first-run auth offer): a terminal with no usable credential opens the
/// <c>/auth</c> picker once after composition, Esc dismisses it without a write, and every
/// non-terminal path keeps the explicit missing-credential error instead of prompting.
/// </summary>
public sealed class InteractiveSessionFirstRunTests
{
    [Fact]
    public async Task A_terminal_with_no_credential_opens_the_auth_picker()
    {
        using var host = new InteractiveSessionHost();
        host.Console.IsInteractive = true;

        Task run = host.RunAsync();
        await WaitForFrameAsync(host, "Select provider");

        ProviderPreset first = ProviderPresets.All[0];
        Assert.Contains($"> {first.Id} — {first.Name}", host.LastFrame, StringComparison.Ordinal);
        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.Empty(host.Client.Requests);

        host.Console.SendEscape();
        await WaitForFrameGoneAsync(host, "Select provider");
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Escape_dismisses_the_first_run_offer_without_a_write()
    {
        using var host = new InteractiveSessionHost();
        host.Console.IsInteractive = true;

        Task run = host.RunAsync();
        await WaitForFrameAsync(host, "Select provider");
        host.Console.SendEscape();
        await WaitForFrameGoneAsync(host, "Select provider");
        int writesAfterDismiss = host.Console.Writes.Count;

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;

        Assert.False(File.Exists(host.Temp.File("auth.json")));
        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.DoesNotContain(
            "Select provider",
            string.Concat(host.Console.Writes.Skip(writesAfterDismiss)),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task A_terminal_with_a_resolved_credential_opens_no_picker()
    {
        using var host = new InteractiveSessionHost(environment: name =>
            name == "OPENAI_API_KEY" ? "sk-test" : PrintModeTestSupport.Environment()(name)
        );
        host.Console.IsInteractive = true;

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;

        Assert.DoesNotContain("Select provider", host.Frames, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_non_interactive_session_with_no_credential_opens_no_picker()
    {
        using var host = new InteractiveSessionHost();

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;

        Assert.DoesNotContain("Select provider", host.Frames, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_interactive_session_defers_the_missing_credential_until_the_first_request()
    {
        var factory = new ThrowingChatClientFactory();
        using var host = new InteractiveSessionHost(factory: factory);
        host.Console.IsInteractive = true;

        Task run = host.RunAsync();
        await WaitForFrameAsync(host, "Select provider");
        Assert.Equal(0, factory.Creates);

        host.Console.SendEscape();
        await WaitForFrameGoneAsync(host, "Select provider");

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "run failed");

        Assert.Equal(1, factory.Creates);
        Assert.Contains(
            "OPENAI_API_KEY environment variable is not set",
            host.Frames,
            StringComparison.Ordinal
        );
        Assert.Contains("no 'openai' key is stored", host.Frames, StringComparison.Ordinal);
        Assert.DoesNotContain("Select provider", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("API key for", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Print_mode_with_a_missing_credential_reports_the_error_without_prompting(
        bool yolo
    )
    {
        using var temp = new TempDirectory();
        var factory = new ThrowingChatClientFactory();
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory) with
        {
            Prompt = "go",
            Yolo = yolo,
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Contains(ThrowingChatClientFactory.Message, errors, StringComparison.Ordinal);
        Assert.Empty(output);
        Assert.DoesNotContain("Select provider", output + errors, StringComparison.Ordinal);
        Assert.DoesNotContain("API key for", output + errors, StringComparison.Ordinal);
    }

    private static Task WaitForFrameAsync(InteractiveSessionHost host, string text) =>
        host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(text, StringComparison.Ordinal);
        });

    private static Task WaitForFrameGoneAsync(InteractiveSessionHost host, string text) =>
        host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains(text, StringComparison.Ordinal);
        });

    /// <summary>
    /// A factory whose creation fails the way a missing credential does; the deferred client makes
    /// the failure happen on the first request instead of during composition.
    /// </summary>
    private sealed class ThrowingChatClientFactory : IChatClientFactory
    {
        internal const string Message =
            "The OPENAI_API_KEY environment variable is not set and no 'openai' key is stored. "
            + "Set OPENAI_API_KEY or add the key to auth.json.";

        public int Creates { get; private set; }

        public IChatClient Create(ModelInfo model)
        {
            Creates++;
            throw new InvalidOperationException(Message);
        }

        public IChatClient Create(ModelInfo model, string? apiKey) => Create(model);
    }
}
