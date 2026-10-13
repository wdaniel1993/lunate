namespace Lunate.Agent.Tests;

internal sealed class RecordingApprover(bool decision) : IToolApprover
{
    public List<(ITool Tool, string Args)> Calls { get; } = [];

    /// <summary>Every ask with its asking-run identity, in order.</summary>
    public List<ApprovalRequest> Requests { get; } = [];

    public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
    {
        Calls.Add((request.Tool, request.Args.GetRawText()));
        Requests.Add(request);
        return ValueTask.FromResult(decision);
    }
}
