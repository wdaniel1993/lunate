using Lunate.Contracts;

namespace Lunate.Extensibility.Abstractions;

/// <summary>Requests validated, model-backed answers to one or more named questions.</summary>
public interface IDecisionService
{
    /// <summary>Evaluates every question independently and returns results keyed by question id.</summary>
    Task<DecisionResponse> DecideAsync(
        DecisionRequest request,
        CancellationToken cancellationToken = default
    );
}
