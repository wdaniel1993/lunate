using System.Text.Json;

namespace Lunate.Agent.Tests;

internal sealed class RecordingApprover(bool decision) : IToolApprover
{
    public List<(ITool Tool, string Args)> Calls { get; } = [];

    public ValueTask<bool> ApproveAsync(ITool tool, JsonElement args, CancellationToken ct)
    {
        Calls.Add((tool, args.GetRawText()));
        return ValueTask.FromResult(decision);
    }
}
