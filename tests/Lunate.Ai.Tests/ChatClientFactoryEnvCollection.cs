namespace Lunate.Ai.Tests;

/// <summary>
/// The client-factory suites drive <c>LUNATE_RECORD</c>/<c>LUNATE_RECORD_PATH</c>/<c>LUNATE_REPLAY</c>
/// through process environment variables, so they run alone: no other collection may observe those
/// variables mid-run (a leaked replay path would silently reroute a sibling factory test through
/// the replay branch).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ChatClientFactoryEnvCollection
{
    public const string Name = "chat-client-factory-env";
}
