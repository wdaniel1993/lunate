using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Lunate.Ai.Tests;

/// <summary>
/// The provider wire: what a chat request actually carries on the way out. These tests drive the
/// factory end to end against a capturing fake transport, so the assertions see the same wire a
/// provider sees, not the SDK objects.
/// </summary>
public sealed class ProviderWireTests
{
    [Fact]
    public async Task A_plain_openai_chat_call_carries_the_lunate_user_agent_and_bears_the_credential()
    {
        using var environment = new EnvironmentScope(("OPENAI_API_KEY", "wire-probe-openai-key"));
        CapturingWire wire = CapturingWire.OpenAi();

        IChatClient client = wire.Factory().Create(ProviderWireTests.OpenAiModel);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            options: null,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(wire.Last);
        Assert.StartsWith("lunate/", wire.Last.Headers.UserAgent.ToString());
        Assert.Equal("Bearer wire-probe-openai-key", wire.HeaderValues("Authorization").Single());
    }

    [Fact]
    public async Task The_openrouter_wire_carries_the_lunate_user_agent_and_attribution_headers()
    {
        using var environment = new EnvironmentScope(
            ("OPENROUTER_API_KEY", "wire-probe-openrouter-key")
        );
        CapturingWire wire = CapturingWire.OpenAi();
        var model = new ModelInfo("vendor-a/chat-model", "openrouter", null, 8_192, true);

        IChatClient client = wire.Factory().Create(model);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            options: null,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(wire.Last);
        Assert.StartsWith("lunate/", wire.Last.Headers.UserAgent.ToString());
        // The attribution headers a non-OpenCode provider documents ride the same pipeline:
        // one headers handler serves every provider, so the UA test is not an OpenCode-only fact.
        Assert.Equal(["https://github.com/wdaniel1993/lunate"], wire.HeaderValues("HTTP-Referer"));
        Assert.Equal(["Lunate"], wire.HeaderValues("X-Title"));
        Assert.Equal(
            "Bearer wire-probe-openrouter-key",
            wire.HeaderValues("Authorization").Single()
        );
    }

    [Fact]
    public async Task An_x_api_key_placement_removes_the_sdk_added_authorization_and_sends_the_key_once()
    {
        using var environment = new EnvironmentScope(("OPENAI_API_KEY", "wire-probe-openai-key"));
        CapturingWire wire = CapturingWire.OpenAi();

        IChatClient client = wire.Factory()
            .Create(OpenAiModel with { CredentialPlacement = "x-api-key" });
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            options: null,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(wire.Last);
        Assert.Null(wire.HeaderValuesOrNull("Authorization")); // The SDK-added sentinel form was removed.
        Assert.Equal(["wire-probe-openai-key"], wire.HeaderValues("x-api-key"));
    }

    [Fact]
    public async Task A_named_header_placement_carries_the_credential_once()
    {
        using var environment = new EnvironmentScope(("OPENAI_API_KEY", "wire-probe-openai-key"));
        CapturingWire wire = CapturingWire.OpenAi();

        IChatClient client = wire.Factory()
            .Create(OpenAiModel with { CredentialPlacement = "header:api-key" });
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            options: null,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(wire.Last);
        Assert.Equal(["wire-probe-openai-key"], wire.HeaderValues("api-key"));
        Assert.Null(wire.HeaderValuesOrNull("Authorization"));
    }

    [Fact]
    public async Task A_query_placement_carries_the_credential_once()
    {
        using var environment = new EnvironmentScope(("OPENAI_API_KEY", "wire-probe-openai-key"));
        CapturingWire wire = CapturingWire.OpenAi();

        IChatClient client = wire.Factory()
            .Create(OpenAiModel with { CredentialPlacement = "query:key" });
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            options: null,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(wire.Last);
        Assert.Contains("key=wire-probe-openai-key", wire.Last.RequestUri!.Query);
        Assert.Null(wire.HeaderValuesOrNull("Authorization"));
    }

    [Fact]
    public async Task Templates_resolve_over_the_create_context_and_the_version()
    {
        using var environment = new EnvironmentScope(("OPENAI_API_KEY", "wire-probe-openai-key"));
        CapturingWire wire = CapturingWire.OpenAi();
        var model = OpenAiModel with
        {
            ProviderHeaders = new Dictionary<string, string>
            {
                ["x-session"] = "{session.id}",
                ["x-version"] = "{lunate.version}",
            },
        };

        IChatClient client = wire.Factory().Create(model, new RequestContext("sess-parent"));
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            options: null,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(wire.Last);
        Assert.Equal(["sess-parent"], wire.HeaderValues("x-session"));
        Assert.StartsWith("lunate/", Assert.Single(wire.HeaderValues("x-version")));
    }

    [Fact]
    public async Task The_active_run_supplies_the_child_sessions_identity()
    {
        using var environment = new EnvironmentScope(("OPENAI_API_KEY", "wire-probe-openai-key"));
        CapturingWire wire = CapturingWire.OpenAi();
        var model = OpenAiModel with
        {
            ProviderHeaders = new Dictionary<string, string> { ["x-session"] = "{session.id}" },
        };

        IChatClient client = wire.Factory()
            .Create(model, new RequestContext("sess-parent", "run-parent"));
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "parent turn")],
            options: null,
            TestContext.Current.CancellationToken
        );

        using (RequestContext.Scope child = RequestContext.Activate("sess-child", "run-child"))
        {
            await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "child turn")],
                options: null,
                TestContext.Current.CancellationToken
            );
        }

        Assert.Equal(
            [
                ["sess-parent"],
                ["sess-child"],
            ],
            wire.ValuesAcrossRequests("x-session")
        );
    }

    [Fact]
    public async Task A_missing_session_id_fails_loudly_naming_the_header()
    {
        using var environment = new EnvironmentScope(("OPENAI_API_KEY", "wire-probe-openai-key"));
        CapturingWire wire = CapturingWire.OpenAi();
        var model = OpenAiModel with
        {
            ProviderHeaders = new Dictionary<string, string> { ["x-session"] = "{session.id}" },
        };

        IChatClient client = wire.Factory().Create(model, context: null);
        InvalidOperationException failure = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () =>
                client.GetResponseAsync(
                    [new ChatMessage(ChatRole.User, "Hi")],
                    options: null,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Contains("'x-session'", failure.Message);
        Assert.Contains("{session.id}", failure.Message);
    }

    [Fact]
    public async Task The_anthropic_messages_wire_carries_x_api_key_and_drops_the_sdk_credential_header()
    {
        CapturingWire wire = CapturingWire.OpenAi();
        var headers = new ProviderHeadersHandler(
            [],
            context: null,
            namedKeySource: null,
            environment: null
        );
        var credential = new ProviderCredentialHandler(
            "anthropic-real-key",
            CredentialPlacement.XApiKey
        );
        var anthropic = new AnthropicClient
        {
            ApiKey = ChatClientFactory.SentinelCredential,
            MaxRetries = 0,
            BaseUrl = "http://localhost",
            HttpClient = new HttpClient(wire.CaptureHandler(CapturingWire.AnthropicMessage))
            {
                BaseAddress = new Uri("http://localhost"),
            },
            Handlers = [headers, credential],
        };

        IChatClient client = anthropic.AsIChatClient("claude-wire");
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            options: null,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(wire.Last);
        Assert.StartsWith("lunate/", wire.Last.Headers.UserAgent.ToString());
        // The SDK's own credential header (its sentinel Bearer) is not on the wire; the declared
        // placement carries the real key exactly once.
        Assert.Null(wire.HeaderValuesOrNull("Authorization"));
        Assert.Equal(["anthropic-real-key"], wire.HeaderValues("x-api-key"));
    }

    [Fact]
    public async Task A_native_transport_can_reuse_the_shared_header_and_credential_handlers()
    {
        using var environment = new EnvironmentScope(("OPENAI_API_KEY", "wire-probe-openai-key"));
        CapturingWire wire = CapturingWire.OpenAi();
        ChatClientFactory factory = wire.Factory();
        using HttpClient client = factory.CreateProviderHttpClient(
            OpenAiModel,
            apiKey: null,
            context: null,
            ProviderApiFamily.OpenAiChat
        );

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            "https://example.test/v1/systemone"
        );
        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(wire.Last);
        Assert.StartsWith("lunate/", wire.Last.Headers.UserAgent.ToString());
        Assert.Equal("Bearer wire-probe-openai-key", wire.HeaderValues("Authorization").Single());
    }

    internal static readonly ModelInfo OpenAiModel = new("gpt-wire", "openai", null, 128_000, true);
}

/// <summary>
/// The fake transport one probe per wire: a capturing handler behind the factory's real OpenAI
/// transport, with a minimal chat-completions response.
/// </summary>
internal sealed class CapturingWire
{
    private readonly List<HttpRequestMessage> _requests = [];

    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    public HttpRequestMessage? Last => _requests.LastOrDefault();

    public IEnumerable<string> HeaderValues(string name)
    {
        HttpRequestMessage request =
            Last ?? throw new InvalidOperationException("no request captured");
        return request.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values!
            : throw new InvalidOperationException(
                $"the header '{name}' was not on the wire: {request.RequestUri}"
            );
    }

    /// <summary>One entry per captured request: the value(s) the header carried, or a null entry.</summary>
    public IReadOnlyList<IReadOnlyList<string>?> ValuesAcrossRequests(string name) =>
        [
            .. _requests.Select(request =>
                request.Headers.TryGetValues(name, out IEnumerable<string>? values)
                    ? (IReadOnlyList<string>?)[.. values]
                    : null
            ),
        ];

    public IEnumerable<string>? HeaderValuesOrNull(string name) =>
        Last is { } last && last.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values
            : null;

    /// <summary>The factory whose OpenAI transport reaches this capture.</summary>
    public static CapturingWire OpenAi() => new(null);

    /// <summary>
    /// The factory whose transport answers the Anthropic Messages shape; the messages family's
    /// probes ride it, because the chat-completions body does not parse as a messages response.
    /// </summary>
    public static CapturingWire Anthropic() => new(AnthropicMessage);

    private readonly string? _factoryResponse;

    private CapturingWire(string? factoryResponse) => _factoryResponse = factoryResponse;

    public ChatClientFactory Factory()
    {
        ChatClientFactory factory = new(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false
        );
        factory.BaseTransportFactory = () => new CapturingTransport(this, _factoryResponse);
        return factory;
    }

    /// <summary>The capturing base the SDK-built chain finally sends through.</summary>
    public HttpMessageHandler CaptureHandler(string? response = null) =>
        new CapturingTransport(this, response);

    private sealed class CapturingTransport(CapturingWire wire, string? response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            wire._requests.Add(request);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        response ?? OpenAiChatCompletion,
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }

    /// <summary>A minimal chat-completions response for the OpenAI paths.</summary>
    internal const string OpenAiChatCompletion = """
        {
            "id": "chatcmpl-wire",
            "object": "chat.completion",
            "created": 0,
            "model": "gpt-wire",
            "choices": [
                {
                    "index": 0,
                    "message": { "role": "assistant", "content": "pong" },
                    "finish_reason": "stop"
                }
            ],
            "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
        }
        """;

    /// <summary>A minimal messages response for the Anthropic paths.</summary>
    internal const string AnthropicMessage = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-wire","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}
        """;
}
