using System.Globalization;
using System.Runtime.InteropServices;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// Composes the system prompt from the embedded <c>system-prompt.md</c> template: the template's
/// named tokens are replaced with the runtime facts (canonical working directory, OS, resolved
/// shell, UTC date, registered tool names) and the <c>AGENTS.md</c> chain. Plain
/// <see cref="string.Replace(string, string, StringComparison)"/> is used deliberately - braces in
/// prose or in user instruction files must never throw, so no formatting engine is involved.
/// </summary>
public static class SystemPrompt
{
    /// <summary>The logical name of the embedded template resource.</summary>
    public const string EmbeddedResourceName = "Lunate.Coding.system-prompt.md";

    /// <summary>Composes the prompt for a workspace; <paramref name="toolNames"/> is in registration order.</summary>
    public static string Compose(
        Workspace workspace,
        IReadOnlyList<string> toolNames,
        ShellResolver? shellResolver = null,
        DateTimeOffset? now = null
    )
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(toolNames);

        string repositoryRoot = workspace.RepoRoot ?? workspace.WorktreeRoot;
        string instructions = AgentsInstructions.Compose(repositoryRoot, workspace.WorktreeRoot);
        string shell = (shellResolver ?? new ShellResolver()).Resolve()?.DisplayName ?? "unknown";
        string tools = toolNames.Count == 0 ? "none" : string.Join(", ", toolNames);
        string date = (now ?? DateFromEnvironment() ?? DateTimeOffset.UtcNow).UtcDateTime.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture
        );

        // The AGENTS.md content is substituted last so user text is never rescanned for tokens.
        return ReadTemplate()
            .Replace("{cwd}", workspace.WorktreeRoot, StringComparison.Ordinal)
            .Replace("{os}", RuntimeInformation.OSDescription, StringComparison.Ordinal)
            .Replace("{shell}", shell, StringComparison.Ordinal)
            .Replace("{tools}", tools, StringComparison.Ordinal)
            .Replace("{date}", date, StringComparison.Ordinal)
            .Replace("{agents}", instructions, StringComparison.Ordinal)
            .TrimEnd();
    }

    /// <summary>
    /// Composes the prompt with the launch directives (design D5): a replacement <c>system</c>
    /// directive is the whole prompt, an <c>append</c> directive is one section after the composed
    /// prompt joined the way the harness joins sections, and neither leaves the bytes unchanged.
    /// </summary>
    internal static string Compose(
        Workspace workspace,
        IReadOnlyList<string> toolNames,
        LaunchOptions directives,
        ShellResolver? shellResolver = null,
        DateTimeOffset? now = null
    )
    {
        ArgumentNullException.ThrowIfNull(directives);

        // The replacement is the caller's whole contract: no template and no tool conventions.
        if (directives.SystemPrompt is { } replacement)
        {
            return replacement;
        }

        string composed = Compose(workspace, toolNames, shellResolver, now);
        return directives.AppendSystemPrompt is { } appended
            ? composed + "\n\n" + appended
            : composed;
    }

    /// <summary>
    /// Records the launch directives on a freshly created session: the replacement as
    /// <c>system</c> and the appendix as <c>system-append</c>, each only when given. Reusing the
    /// existing prompt-section entry keeps the session format and schema unchanged.
    /// </summary>
    internal static void Record(Session session, LaunchOptions directives)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(directives);

        if (directives.SystemPrompt is { } replacement)
        {
            session.AppendPromptSection("system", replacement);
        }

        if (directives.AppendSystemPrompt is { } appended)
        {
            session.AppendPromptSection("system-append", appended);
        }
    }

    /// <summary>
    /// The effective directives for a run: an explicit CLI directive wins over the latest recorded
    /// section, which wins over the composed prompt (design D5).
    /// </summary>
    internal static LaunchOptions Directives(LaunchOptions launch, Session? session)
    {
        ArgumentNullException.ThrowIfNull(launch);
        return launch with
        {
            SystemPrompt = launch.SystemPrompt ?? RecordedText(session, "system"),
            AppendSystemPrompt =
                launch.AppendSystemPrompt ?? RecordedText(session, "system-append"),
        };
    }

    private static string? RecordedText(Session? session, string section) =>
        session
            ?.Entries.OfType<SessionPromptSectionEntry>()
            .LastOrDefault(entry => string.Equals(entry.Section, section, StringComparison.Ordinal))
            ?.Text;

    internal static string ReadTemplate()
    {
        using Stream? stream = typeof(SystemPrompt).Assembly.GetManifestResourceStream(
            EmbeddedResourceName
        );
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The embedded resource '{EmbeddedResourceName}' was not found."
            );
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The date pin for deterministic sessions: <c>LUNATE_DATE</c> (an ISO <c>yyyy-MM-dd</c> date)
    /// replaces the current UTC date in the composed prompt, so a replay fixture's request digest
    /// stays stable across days. An invalid value fails loudly — a silent fallback would make the
    /// resulting fixture mismatch look like a different bug.
    /// </summary>
    private static DateTimeOffset? DateFromEnvironment()
    {
        string? value = Environment.GetEnvironmentVariable("LUNATE_DATE");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (
            !DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed
            )
        )
        {
            throw new InvalidOperationException(
                $"The LUNATE_DATE environment variable ('{value}') is not a valid date; use yyyy-MM-dd."
            );
        }

        return parsed;
    }
}
