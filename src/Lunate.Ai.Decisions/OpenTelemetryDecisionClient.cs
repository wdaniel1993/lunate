using System.Diagnostics;

namespace Lunate.Ai.Decisions;

internal sealed class OpenTelemetryDecisionClient(IDecisionClient innerClient)
    : DelegatingDecisionClient(innerClient)
{
    private static readonly ActivitySource Source = new("Lunate.Ai.Decisions");

    public override async Task<DecisionResult> GetDecisionAsync(
        DecisionRequest request,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        using Activity? activity = Source.StartActivity("decision", ActivityKind.Client);
        activity?.SetTag("gen_ai.request.model", options?.ModelId ?? request.ModelId);
        activity?.SetTag("gen_ai.operation.name", "decision");
        activity?.SetTag("lunate.decision.question_count", request.Questions.Count);
        try
        {
            DecisionResult result = await base.GetDecisionAsync(request, options, cancellationToken)
                .ConfigureAwait(false);
            activity?.SetTag("lunate.decision.outcome", "success");
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetTag("lunate.decision.outcome", "error");
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
    }
}
