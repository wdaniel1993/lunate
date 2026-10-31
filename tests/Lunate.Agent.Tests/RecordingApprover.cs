namespace Lunate.Agent.Tests;

internal sealed class RecordingApprover(bool decision) : IUserPromptToolApprover
{
    public List<(ITool Tool, string Args)> Calls { get; } = [];

    /// <summary>Every ask with its asking-run identity, in order.</summary>
    public List<ApprovalRequest> Requests { get; } = [];

    /// <summary>Escalated asks routed through the explicit prompt capability.</summary>
    public List<ApprovalRequest> PromptRequests { get; } = [];

    public ValueTask<bool> PromptAsync(ApprovalRequest request, CancellationToken ct)
    {
        PromptRequests.Add(request);
        return ValueTask.FromResult(decision);
    }

    public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
    {
        Calls.Add((request.Tool, request.Args.GetRawText()));
        Requests.Add(request);
        return ValueTask.FromResult(decision);
    }
}
