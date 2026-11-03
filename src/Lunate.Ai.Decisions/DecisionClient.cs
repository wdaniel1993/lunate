using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Decisions;

internal interface IDecisionClient
{
    Task<DecisionResult> GetDecisionAsync(
        DecisionRequest request,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default
    );

    object? GetService(Type serviceType);
}

internal class DelegatingDecisionClient(IDecisionClient innerClient) : IDecisionClient
{
    protected IDecisionClient InnerClient { get; } =
        innerClient ?? throw new ArgumentNullException(nameof(innerClient));

    public virtual Task<DecisionResult> GetDecisionAsync(
        DecisionRequest request,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default
    ) => InnerClient.GetDecisionAsync(request, options, cancellationToken);

    public virtual object? GetService(Type serviceType) => InnerClient.GetService(serviceType);
}

internal sealed record DecisionRequest(
    string ModelId,
    JsonElement State,
    IReadOnlyList<DecisionQuestion> Questions
);

internal sealed record DecisionQuestion(
    string Id,
    string Instructions,
    DecisionQuestionKind Kind,
    IReadOnlyList<string>? Answers = null,
    IReadOnlyDictionary<string, string>? Criteria = null,
    string? JsonPath = null,
    JsonElement? State = null
);

internal enum DecisionQuestionKind
{
    Noul,
    Choice,
    Score,
    Box,
}

internal sealed record DecisionOptions(
    string? ModelId = null,
    TimeSpan? Timeout = null,
    string? SessionId = null,
    string? RunId = null
);

internal sealed record DecisionProbability(string Answer, double Probability);

internal sealed record DecisionBox(double Top, double Bottom, double Left, double Right);

internal sealed record DecisionAnswer(
    string Answer,
    double Probability,
    IReadOnlyList<DecisionProbability> Distribution,
    string ModelId,
    string Method,
    double Confidence,
    DecisionBox? Box = null,
    double? ExpectedScore = null
);

internal sealed record DecisionUsage(
    long? InputTokens = null,
    long? OutputTokens = null,
    double? Cost = null,
    bool IsProviderReported = false
);

internal sealed record DecisionResult(
    IReadOnlyDictionary<string, DecisionAnswer> Results,
    DecisionUsage? Usage = null,
    IReadOnlyList<UsageDetails>? ChatUsageAttempts = null
);

internal static class DecisionClientServiceExtensions
{
    public static TService? GetService<TService>(this IDecisionClient client)
        where TService : class => client.GetService(typeof(TService)) as TService;
}
