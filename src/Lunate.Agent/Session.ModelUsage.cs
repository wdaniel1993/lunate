namespace Lunate.Agent;

public sealed partial class Session
{
    /// <summary>Records an attributed model call that did not produce a transcript message.</summary>
    public void AppendModelUsage(
        string? model,
        SessionUsage? usage,
        ModelUsageAttribution? attribution,
        string? decisionOption = null
    )
    {
        if (Header.Schema < 4)
        {
            throw new InvalidOperationException(
                "Model usage entries require a schema-4 session; older sessions remain unchanged."
            );
        }

        AddEntry(
            new SessionModelUsageEntry(
                NextEntryId(),
                LastEntryId,
                _timeProvider.GetUtcNow(),
                model,
                usage,
                attribution,
                decisionOption
            )
        );
    }
}
