using System.Text.Json;

namespace Lunate.Contracts;

/// <summary>A batch of independent questions evaluated against one text or JSON state.</summary>
public sealed record DecisionRequest(JsonElement State, IReadOnlyList<DecisionQuestion> Questions);

/// <summary>One named decision question; choice answers and score levels retain their declared order.</summary>
public sealed record DecisionQuestion(
    string Id,
    string Text,
    DecisionQuestionKind Kind,
    IReadOnlyList<string>? Answers = null,
    string? JsonPath = null
);

/// <summary>The four question kinds supported by the decision primitive.</summary>
public enum DecisionQuestionKind
{
    YesNo,
    Choice,
    Score,
    Box,
}

/// <summary>The ordered probability assigned to one defined answer.</summary>
public sealed record DecisionProbability(string Answer, double Probability);

/// <summary>A validated answer and the provenance of its model exchange.</summary>
public sealed record DecisionAnswer(
    string Answer,
    double Probability,
    IReadOnlyList<DecisionProbability> Distribution,
    string ModelId,
    string Method
)
{
    /// <summary>The computed confidence used by routing and decision consumers.</summary>
    public double Confidence { get; init; }

    /// <summary>The normalized image rectangle for a box question; null for other kinds.</summary>
    public DecisionBox? Box { get; init; }
}

/// <summary>A normalized image rectangle and the model's confidence in its placement.</summary>
public sealed record DecisionBox(
    double Top,
    double Bottom,
    double Left,
    double Right,
    double Confidence
);

/// <summary>Validated answers keyed by question id.</summary>
public sealed record DecisionResponse(IReadOnlyDictionary<string, DecisionAnswer> Results);

/// <summary>A provider-native decision endpoint exposed by an <c>IChatClient</c>.</summary>
public interface INativeDecisionClient
{
    /// <summary>
    /// Evaluates the original request and returns raw JSON shaped as a <c>results</c> object keyed by
    /// question id; each value has an <c>answer</c> and a <c>probabilities</c> object.
    /// </summary>
    Task<string> GetDecisionAsync(
        DecisionRequest request,
        CancellationToken cancellationToken = default
    );
}

internal interface IAgentDecisionService
{
    Task<DecisionResponse> DecideAsync(
        DecisionRequest request,
        CancellationToken cancellationToken = default
    );
}
