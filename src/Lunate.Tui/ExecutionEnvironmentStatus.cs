namespace Lunate.Tui;

/// <summary>
/// The selected container environment and its readiness, for the welcome header and
/// <c>/context</c>: the header names the selection on its own line, and <c>/context</c> adds
/// whether the container is still to be created (<c>starting</c>), has run (<c>ready</c>) or could
/// not be created (<c>failed: &lt;reason&gt;</c>). Local execution has no status at all, so neither
/// surface gains an environment segment for the default.
/// </summary>
internal sealed record ExecutionEnvironmentStatus(string Description, string Readiness)
{
    internal const string Starting = "starting";

    internal const string Ready = "ready";

    internal const string FailedPrefix = "failed: ";

    internal static ExecutionEnvironmentStatus StartingFor(string description) =>
        new(description, Starting);

    internal static ExecutionEnvironmentStatus ReadyFor(string description) =>
        new(description, Ready);

    internal static ExecutionEnvironmentStatus FailedFor(string description, string reason) =>
        new(description, FailedPrefix + reason);

    /// <summary>The header's own line: <c>environment podman:alpine</c>.</summary>
    internal string HeaderText => "environment " + Description;

    /// <summary>The <c>/context</c> segment: <c>environment: podman:alpine (ready)</c>.</summary>
    internal string ContextText => $"environment: {Description} ({Readiness})";
}
