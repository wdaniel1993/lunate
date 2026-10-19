using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Lunate.Ai.Tests;

[Collection(ChatClientFactoryEnvCollection.Name)]
public sealed class OpenAiEffortTransportTests
{
    private const string Completion = """
        {
            "id": "chatcmpl-1",
            "object": "chat.completion",
            "created": 0,
            "model": "gpt-test",
            "choices": [
                {
                    "index": 0,
                    "message": { "role": "assistant", "content": "pong" },
                    "finish_reason": "stop"
                }
            ],
            "usage": { "prompt_tokens": 3, "completion_tokens": 1, "total_tokens": 4 }
        }
        """;

    [Fact]
    public async Task Effort_style_sends_the_reasoning_effort_on_the_wire()
    {
        var handler = new CapturingHandler(Completion);
        var model = new ModelInfo(
            "gpt-test",
            "openai",
            null,
            128_000,
            true,
            AuthRef: null,
            Reasoning: "effort"
        );
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(static () => { }),
            enableOpenTelemetry: false,
            providerClientFactory: (clientModel, _) => CreateOpenAiClient(clientModel.Id, handler)
        );
        ChatOptions options = new();
        ReasoningParameters.Apply(options, model, ThinkingLevel.High);
        IChatClient client = factory.Create(model);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            options,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(handler.RequestBody);
        using JsonDocument document = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal("high", document.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal("gpt-test", document.RootElement.GetProperty("model").GetString());
    }

    private static IChatClient CreateOpenAiClient(string modelId, HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        OpenAIClientOptions options = new()
        {
            Transport = new HttpClientPipelineTransport(httpClient),
        };
        return new OpenAIClient(new ApiKeyCredential("test-key"), options)
            .GetChatClient(modelId)
            .AsIChatClient();
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
