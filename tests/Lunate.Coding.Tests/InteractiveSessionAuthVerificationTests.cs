using Lunate.Ai;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The optional post-save verification: it asks first, runs exactly one non-streaming request on
/// the saved provider's model, reports success, and on failure reports without discarding the key.
/// </summary>
public sealed class InteractiveSessionAuthVerificationTests
{
    private const string Secret = "groq-secret-value";

    [Fact]
    public async Task The_question_opens_on_skip_and_Esc_runs_no_request()
    {
        var spy = new SpyChatClient();
        var factory = new FakeChatClientFactory(spy);
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            factory: factory,
            secretPrompt: new ScriptedSecretPrompt(Secret)
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await SaveGroqAsync(host);

        Assert.Contains("> Skip verification", host.LastFrame, StringComparison.Ordinal);
        Assert.Equal(0, spy.NonStreamingCalls);

        host.Console.SendEscape();
        await WaitForFrameGoneAsync(host, "Verify groq with one request?");

        Assert.Equal(0, spy.NonStreamingCalls);
        Assert.Null(host.Session.VerifyTask);
        Assert.Equal(Secret, AuthStore.Load(host.Temp.File("auth.json"), _ => null).TryGet("groq"));
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Yes_runs_exactly_one_minimal_request_against_the_saved_provider()
    {
        var spy = new SpyChatClient();
        var factory = new FakeChatClientFactory(spy);
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            factory: factory,
            secretPrompt: new ScriptedSecretPrompt(Secret)
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await SaveGroqAsync(host);

        host.Console.SendUp();
        await WaitForFrameAsync(host, "> Yes, send one request");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "verified groq with one request");

        await host.Session.VerifyTask!.WaitAsync(
            InteractiveSessionHost.DefaultWait,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, spy.NonStreamingCalls);
        Assert.Equal(0, spy.StreamingCalls);
        Assert.Equal(1, spy.Options?.MaxOutputTokens);
        Assert.Equal("groq", factory.CreatedModel?.Provider);
        ChatMessage message = Assert.Single(spy.Messages!);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Equal("Reply with OK.", message.Text);
        Assert.Equal(Secret, AuthStore.Load(host.Temp.File("auth.json"), _ => null).TryGet("groq"));
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_failed_verification_reports_without_the_key_and_keeps_it()
    {
        var spy = new SpyChatClient
        {
            Failure = new InvalidOperationException("the key " + Secret + " was rejected"),
        };
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            factory: new FakeChatClientFactory(spy),
            secretPrompt: new ScriptedSecretPrompt(Secret)
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await SaveGroqAsync(host);

        host.Console.SendUp();
        await WaitForFrameAsync(host, "> Yes, send one request");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "verification failed for groq");

        await host.Session.VerifyTask!.WaitAsync(
            InteractiveSessionHost.DefaultWait,
            TestContext.Current.CancellationToken
        );

        Assert.Contains("[redacted]", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, host.Frames, StringComparison.Ordinal);
        Assert.Equal(Secret, AuthStore.Load(host.Temp.File("auth.json"), _ => null).TryGet("groq"));
        host.Console.Complete();
        await run;
    }

    private static async Task SaveGroqAsync(InteractiveSessionHost host)
    {
        host.Console.SendText("/auth");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "Select provider");

        int index = ProviderPresets.All.ToList().FindIndex(preset => preset.Id == "groq");
        for (var press = 0; press < index; press++)
        {
            host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        }

        await WaitForFrameAsync(host, "> groq — Groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "logged in: groq");
        await WaitForFrameAsync(host, "Verify groq with one request?");
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

    private sealed class ScriptedSecretPrompt(params string?[] values) : ISecretPrompt
    {
        private readonly Queue<string?> _values = new(values);

        public Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken) =>
            Task.FromResult(_values.Count > 0 ? _values.Dequeue() : null);
    }

    private sealed class SpyChatClient : IChatClient
    {
        public int NonStreamingCalls { get; private set; }

        public int StreamingCalls { get; private set; }

        public ChatOptions? Options { get; private set; }

        public IReadOnlyList<ChatMessage>? Messages { get; private set; }

        public Exception? Failure { get; init; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            NonStreamingCalls++;
            Options = options;
            Messages = [.. messages];
            return Failure is null
                ? Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK")))
                : Task.FromException<ChatResponse>(Failure);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default
        )
        {
            StreamingCalls++;
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
