using System.Diagnostics;
using System.Text.Json;
using Lunate.Ai.Decisions;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class DecisionClientTests
{
    [Fact]
    [Trait("Category", "DecisionLayer")]
    public void Client_builder_returns_the_configured_client()
    {
        DecisionResult expected = new(new Dictionary<string, DecisionAnswer>());
        StubDecisionClient client = new(expected);

        IDecisionClient built = new DecisionClientBuilder().UseClient(client).Build();

        Assert.Same(client, built);
    }

    [Fact]
    [Trait("Category", "DecisionLayer")]
    public void Known_input_modalities_override_the_legacy_vision_flag()
    {
        Lunate.Ai.ModelInfo model = new(
            "text-only-model",
            "test-provider",
            null,
            1024,
            SupportsTools: false
        )
        {
            SupportsVision = true,
            Capabilities = new Lunate.Ai.ModelCapabilityDescriptor(
                inputModalities: new HashSet<string>(StringComparer.Ordinal)
            ),
        };

        Assert.False(DecisionClientResolver.SupportsImageInput(model));
    }

    [Fact]
    [Trait("Category", "DecisionLayer")]
    public async Task OpenTelemetry_records_decision_metadata_without_state_or_question_text()
    {
        List<Activity> stopped = [];
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "Lunate.Ai.Decisions",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using JsonDocument state = JsonDocument.Parse("{\"secret\":\"do not record\"}");
        DecisionRequest request = new(
            "decision-model",
            state.RootElement.Clone(),
            [
                new DecisionQuestion(
                    "secret-question",
                    "private question text",
                    DecisionQuestionKind.Noul,
                    ["yes", "no"]
                ),
            ]
        );
        IDecisionClient client = new DecisionClientBuilder()
            .UseClient(
                new StubDecisionClient(new DecisionResult(new Dictionary<string, DecisionAnswer>()))
            )
            .UseOpenTelemetry()
            .Build();

        await client.GetDecisionAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Activity activity = Assert.Single(stopped);
        Assert.Equal("decision-model", activity.GetTagItem("gen_ai.request.model"));
        Assert.Equal(1, activity.GetTagItem("lunate.decision.question_count"));
        Assert.DoesNotContain(
            activity.TagObjects,
            tag => tag.Value?.ToString()?.Contains("private", StringComparison.Ordinal) == true
        );
        Assert.DoesNotContain(
            activity.TagObjects,
            tag =>
                tag.Value?.ToString()?.Contains("do not record", StringComparison.Ordinal) == true
        );
    }

    [Fact]
    [Trait("Category", "DecisionLayer")]
    public async Task Chat_adapter_returns_a_validated_decision()
    {
        using JsonDocument state = JsonDocument.Parse("{\"path\":\"src\"}");
        DecisionRequest request = new(
            "decision-model",
            state.RootElement.Clone(),
            [
                new DecisionQuestion(
                    "allow",
                    "Is this safe?",
                    DecisionQuestionKind.Noul,
                    ["yes", "no"]
                ),
            ]
        );
        ScriptedChatClient chat = new ScriptedChatClient().Enqueue(
            LoopScripts.Text(
                """{"results":{"allow":{"answer":"yes","probabilities":{"yes":0.8,"no":0.2}}}}"""
            )
        );

        DecisionResult result = await new ChatDecisionClient(chat).GetDecisionAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken
        );
        DecisionAnswer answer = result.Results["allow"];

        Assert.Equal("yes", answer.Answer);
        Assert.Equal(0.8d, answer.Probability);
        Assert.Equal(0.6d, answer.Confidence, precision: 12);
        Assert.Equal("decision-model", answer.ModelId);
        Assert.Equal("adapter", answer.Method);
    }

    [Fact]
    [Trait("Category", "DecisionLayer")]
    public async Task Agent_uses_a_native_decision_client_instead_of_chat()
    {
        using JsonDocument state = JsonDocument.Parse("{}");
        Lunate.Contracts.DecisionRequest request = new(
            state.RootElement.Clone(),
            [new("allow", "Is this safe?", Lunate.Contracts.DecisionQuestionKind.YesNo)]
        );
        DecisionAnswer answer = new(
            "yes",
            1d,
            [new("yes", 1d), new("no", 0d)],
            "decision-model",
            "native",
            1d
        );
        StubDecisionClient native = new(
            new DecisionResult(new Dictionary<string, DecisionAnswer> { ["allow"] = answer })
        );
        ScriptedChatClient chat = new ScriptedChatClient { DecisionClient = native }.Enqueue(
            LoopScripts.Text(
                """{"results":{"allow":{"answer":"no","probabilities":{"yes":0.1,"no":0.9}}}}"""
            )
        );

        Lunate.Contracts.DecisionResponse result = await DecisionEngine.EvaluateAsync(
            request,
            chat,
            "decision-model",
            "native",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("native", result.Results["allow"].Method);
        Assert.Empty(chat.Requests);
        Assert.NotNull(native.Request);
    }

    [Fact]
    [Trait("Category", "DecisionLayer")]
    public async Task Legacy_native_client_retries_one_off_contract_response()
    {
        using JsonDocument state = JsonDocument.Parse("{}");
        Lunate.Contracts.DecisionRequest request = new(
            state.RootElement.Clone(),
            [new("allow", "Is this safe?", Lunate.Contracts.DecisionQuestionKind.YesNo)]
        );
        StubLegacyNativeDecisionClient native = new(
            "not json",
            """{"results":{"allow":{"answer":"yes","probabilities":{"yes":0.9,"no":0.1}}}}"""
        );
        ScriptedChatClient chat = new() { NativeDecisionClient = native };

        Lunate.Contracts.DecisionResponse result = await DecisionEngine.EvaluateAsync(
            request,
            chat,
            "decision-model",
            "native",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, native.Requests);
        Assert.Equal("yes", result.Results["allow"].Answer);
        Assert.Equal("native", result.Results["allow"].Method);
        Assert.Empty(chat.Requests);
    }

    [Fact]
    [Trait("Category", "DecisionLayer")]
    public async Task Delegating_client_forwards_the_request_options_and_cancellation()
    {
        using JsonDocument state = JsonDocument.Parse("{}");
        DecisionRequest request = new("model", state.RootElement.Clone(), []);
        DecisionOptions options = new(ModelId: "override");
        DecisionResult expected = new(new Dictionary<string, DecisionAnswer>());
        StubDecisionClient inner = new(expected);
        DelegatingDecisionClient client = new(inner);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        DecisionResult result = await client.GetDecisionAsync(request, options, cancellationToken);

        Assert.Same(expected, result);
        Assert.Same(request, inner.Request);
        Assert.Same(options, inner.Options);
        Assert.Equal(cancellationToken, inner.CancellationToken);
    }

    private sealed class StubDecisionClient(DecisionResult result) : IDecisionClient
    {
        public DecisionRequest? Request { get; private set; }

        public DecisionOptions? Options { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<DecisionResult> GetDecisionAsync(
            DecisionRequest request,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            Options = options;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }

        public object? GetService(Type serviceType) => null;
    }

    private sealed class StubLegacyNativeDecisionClient(params string[] responses)
        : Lunate.Contracts.INativeDecisionClient
    {
        private readonly Queue<string> _responses = new(responses);

        public int Requests { get; private set; }

        public Task<string> GetDecisionAsync(
            Lunate.Contracts.DecisionRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Requests++;
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
