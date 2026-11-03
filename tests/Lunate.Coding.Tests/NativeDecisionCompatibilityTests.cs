using Lunate.Ai.Decisions;
using Lunate.Contracts;
using Microsoft.Extensions.AI;
using InternalDecisionAnswer = Lunate.Ai.Decisions.DecisionAnswer;
using InternalDecisionProbability = Lunate.Ai.Decisions.DecisionProbability;
using InternalDecisionRequest = Lunate.Ai.Decisions.DecisionRequest;
using InternalDecisionResult = Lunate.Ai.Decisions.DecisionResult;

namespace Lunate.Coding.Tests;

public sealed class NativeDecisionCompatibilityTests
{
    [Fact]
    public async Task Chat_service_exposes_the_legacy_interface_as_a_decision_client_forwarder()
    {
        RecordingDecisionClient decisionClient = new();
        NativeDecisionClientCompatibilityChatClient client = new(
            new DecisionOnlyChatClient(decisionClient),
            "model-x"
        );
        INativeDecisionClient legacy = Assert.IsAssignableFrom<INativeDecisionClient>(
            client.GetService(typeof(INativeDecisionClient))
        );
        using System.Text.Json.JsonDocument state = System.Text.Json.JsonDocument.Parse("{}");

        string response = await legacy.GetDecisionAsync(
            new Lunate.Contracts.DecisionRequest(
                state.RootElement.Clone(),
                [new Lunate.Contracts.DecisionQuestion("safe", "Is it safe?", Lunate.Contracts.DecisionQuestionKind.YesNo)]
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Contains("\"answer\":\"yes\"", response, StringComparison.Ordinal);
        Assert.Equal("model-x", decisionClient.Request!.ModelId);
    }

    private sealed class DecisionOnlyChatClient(IDecisionClient decisionClient) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IDecisionClient) ? decisionClient : null;

        public void Dispose() { }
    }

    private sealed class RecordingDecisionClient : IDecisionClient
    {
        public InternalDecisionRequest? Request { get; private set; }

        public Task<InternalDecisionResult> GetDecisionAsync(
            InternalDecisionRequest request,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            return Task.FromResult(
                new InternalDecisionResult(
                    new Dictionary<string, InternalDecisionAnswer>
                    {
                        ["safe"] = new(
                            "yes",
                            0.8,
                            [
                                new InternalDecisionProbability("yes", 0.8),
                                new InternalDecisionProbability("no", 0.2),
                            ],
                            request.ModelId,
                            "native",
                            0.6
                        ),
                    }
                )
            );
        }

        public object? GetService(Type serviceType) => null;
    }
}
