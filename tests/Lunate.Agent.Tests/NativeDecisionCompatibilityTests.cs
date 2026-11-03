using System.Text.Json;
using Lunate.Ai.Decisions;
using Lunate.Contracts;
using ContractDecisionQuestion = Lunate.Contracts.DecisionQuestion;
using ContractDecisionQuestionKind = Lunate.Contracts.DecisionQuestionKind;

namespace Lunate.Agent.Tests;

public sealed class NativeDecisionCompatibilityTests
{
    [Fact]
    public async Task Legacy_native_contract_forwards_requests_to_the_decision_client()
    {
        using JsonDocument state = JsonDocument.Parse("{\"path\":\"src\"}");
        Lunate.Contracts.DecisionRequest request = new(
            state.RootElement.Clone(),
            [new ContractDecisionQuestion("safe", "Is this safe?", ContractDecisionQuestionKind.YesNo)]
        );
        RecordingDecisionClient inner = new();
        INativeDecisionClient adapter = new NativeDecisionClientForwarder(
            inner,
            "model-x"
        );

        string response = await adapter.GetDecisionAsync(
            request,
            TestContext.Current.CancellationToken
        );

        using JsonDocument parsed = JsonDocument.Parse(response);
        JsonElement result = parsed.RootElement.GetProperty("results").GetProperty("safe");
        Assert.Equal("yes", result.GetProperty("answer").GetString());
        Assert.Equal(0.8, result.GetProperty("probabilities").GetProperty("yes").GetDouble());
        Assert.Equal("model-x", inner.Request!.ModelId);
        Assert.Equal(Lunate.Ai.Decisions.DecisionQuestionKind.Noul, inner.Request.Questions[0].Kind);
    }

    private sealed class RecordingDecisionClient : IDecisionClient
    {
        public Lunate.Ai.Decisions.DecisionRequest? Request { get; private set; }

        public Task<Lunate.Ai.Decisions.DecisionResult> GetDecisionAsync(
            Lunate.Ai.Decisions.DecisionRequest request,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            return Task.FromResult(
                new Lunate.Ai.Decisions.DecisionResult(
                    new Dictionary<string, Lunate.Ai.Decisions.DecisionAnswer>
                    {
                        ["safe"] = new(
                            "yes",
                            0.8,
                            [
                                new Lunate.Ai.Decisions.DecisionProbability("yes", 0.8),
                                new Lunate.Ai.Decisions.DecisionProbability("no", 0.2),
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
