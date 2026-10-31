using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private void RecordDecisionUsage(UsageUpdated usage)
    {
        if (_session is not { Schema: >= 4 } session || usage.Attribution is not { } attribution)
        {
            return;
        }

        UsageDetails details = usage.Usage;
        int? cacheRead = details.CachedInputTokenCount is > 0 and long reportedRead
            ? (int)reportedRead
            : null;
        int? cacheWrite =
            details.AdditionalCounts is { } counts
            && counts.TryGetValue(CacheCreationInputTokensKey, out long reportedWrite)
            && reportedWrite > 0
                ? (int)reportedWrite
                : null;
        var recorded = new SessionUsage(
            (int)(details.InputTokenCount ?? 0),
            (int)(details.OutputTokenCount ?? 0),
            cacheRead,
            cacheWrite
        );

        session.AppendModelUsage(usage.ModelId, recorded, attribution);
    }
}
