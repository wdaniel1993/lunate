using System.Reflection;

namespace Lunate.Ai;

/// <summary>
/// The identity a request's wire decorations name: header templates resolve over it
/// ({<c>session.id</c>}, {<c>run.id</c>}) and {<c>lunate.version</c>} comes from the assembly.
/// The interactive session passes its session id and run id; a child session passes its own.
/// Resolution happens at request time, not client construction, so a client can outlive a run:
/// the eight wire handlers read <see cref="Current"/> first and fall back to the context their
/// factory call carried, so a call made outside a run (a probe or a verify request) names the
/// session's own id while every model call of a run names that run's pair. A template that needs
/// a value the context does not carry fails loudly instead of sending an empty decoration.
/// </summary>
public sealed record RequestContext(string? SessionId, string? RunId = null)
{
    private static readonly AsyncLocal<RequestContext?> Ambient = new();

    /// <summary>The context of the run currently executing on this async flow, or null.</summary>
    internal static RequestContext? Current => Ambient.Value;

    /// <summary>
    /// Activates a run's context for the calling async flow; the returned scope restores the
    /// previous value, so nested (child) runs and sequential runs on shared flows each keep
    /// their own pair.
    /// </summary>
    internal static Scope Activate(string? sessionId, string? runId)
    {
        RequestContext? previous = Ambient.Value;
        Ambient.Value = new RequestContext(sessionId, runId);
        return new Scope(previous);
    }

    internal readonly struct Scope(RequestContext? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }

    /// <summary>
    /// The product identity the <c>{lunate.version}</c> template and the user agent both render:
    /// <c>lunate/&lt;version&gt;</c>, the informational version without its build metadata.
    /// </summary>
    internal static string LunateVersion { get; } = "lunate/" + ReadVersion();

    private static string ReadVersion()
    {
        Assembly assembly = typeof(RequestContext).Assembly;
        string informational =
            assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        int metadata = informational.IndexOf('+', StringComparison.Ordinal);
        // The build metadata stays out of the header: the version classifies the release, and a
        // developer tree's rebuild count would make the UA churn per build.
        return metadata < 0 ? informational : informational[..metadata];
    }
}
