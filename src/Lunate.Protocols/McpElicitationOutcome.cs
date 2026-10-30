namespace Lunate.Protocols;

/// <summary>Tracks no-user elicitation cancellations while one MCP tool call is in flight.</summary>
internal sealed class McpElicitationOutcome
{
    private int version;

    internal int Version => Volatile.Read(ref version);

    internal void RecordUnavailable() => Interlocked.Increment(ref version);

    internal bool WasRecordedSince(int previousVersion) => Version > previousVersion;
}
