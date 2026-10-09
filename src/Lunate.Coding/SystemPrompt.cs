using System.Globalization;
using System.Runtime.InteropServices;

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
        string date = (now ?? DateTimeOffset.UtcNow).UtcDateTime.ToString(
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
}
