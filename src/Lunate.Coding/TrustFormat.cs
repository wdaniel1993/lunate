using Lunate.Extensibility;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>
/// The one rendering of trust decisions: the live-area questions, the <c>/trust</c> listing and the
/// trust command all use it, so the surfaces cannot drift. Every line is single-line and
/// invariant-cultured.
/// </summary>
internal static class TrustFormat
{
    internal const int ShortHashLength = 8;

    private const int MaxCommandLength = 60;

    internal static string RecordsText(IReadOnlyList<TrustRecord> records) =>
        records.Count == 0
            ? "trust: no records for this repository"
            : "trust: " + string.Join("; ", records.Select(RecordText));

    internal static string RecordText(TrustRecord record)
    {
        string kind = record.Section switch
        {
            TrustSection.McpConfig => "mcp.json",
            TrustSection.McpServer => "mcp server",
            TrustSection.Skills => "skills",
            _ => "extensions",
        };
        return $"{kind} — {record.WorktreePath} {ShortHash(record.ContentHash)}";
    }

    internal static string ShortHash(string hash) =>
        hash.Length > ShortHashLength ? hash[..ShortHashLength] : hash;

    /// <summary>
    /// The interactive extension question; a re-prompt says the files changed. An out-of-process
    /// extension additionally states that it runs with the user's privileges (the process
    /// boundary is isolation, not a sandbox). A user-scope extension someone installed is not a
    /// project extension and is not called one: it asks because an install is not a decision.
    /// </summary>
    internal static string ExtensionQuestion(ExtensionDescriptor descriptor, bool filesChanged)
    {
        string directory = RealPath(descriptor.Directory!);
        string privileges = descriptor.Manifest.Runtime is not null
            ? " — it runs with your user privileges"
            : "";
        string what =
            descriptor.Scope is ExtensionScope.Global
                ? $"extension '{descriptor.Id}' {descriptor.Version} installed in '{directory}'"
                : $"project extension '{descriptor.Id}' {descriptor.Version} from {directory}";
        if (descriptor.Scope is ExtensionScope.Project)
        {
            return filesChanged
                ? $"Load {what} — its files changed since this repository trusted it{privileges}. Load the new version"
                : $"Load {what}{privileges}";
        }

        return filesChanged
            ? $"Load {what} — its files changed since it was approved{privileges}. Load the new version"
            : $"Load {what}{privileges}";
    }

    /// <summary>
    /// The real path shown in a trust question: absolute, with <c>..</c> and symbolic links
    /// resolved, so the deciding segment cannot hide behind a spelling. A path that is not rooted,
    /// cannot be resolved, or is a clean spelling that does not exist yet (a cosmetic
    /// <c>/home</c>, <c>/var</c> or placeholder path) is shown as given.
    /// </summary>
    internal static string RealPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return path;
        }

        // A missing path has nothing to resolve; canonicalizing would only rewrite a system
        // alias (macOS /home, /var) and make the cosmetic path worse.
        if (!HasParentSegment(path) && !Path.Exists(path))
        {
            return path;
        }

        try
        {
            return Workspace.Canonicalize(path);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return path;
        }
    }

    private static bool HasParentSegment(string path) =>
        path.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries
            )
            .Contains("..");

    /// <summary>
    /// The question's detail lines: the one provenance line an installed extension carries (design
    /// D7), so the prompt, the trust report and the tests read one wording.
    /// </summary>
    internal static IReadOnlyList<string> ProvenanceLine(string? fromPackage) =>
        fromPackage is { Length: > 0 } ? [fromPackage] : [];

    internal static string McpQuestionTitle(string configPath, int serverCount) =>
        FormattableString.Invariant(
            $"Start {serverCount} {(serverCount == 1 ? "server" : "servers")} from project mcp.json '{RealPath(configPath)}'"
        );

    internal static IReadOnlyList<string> McpQuestionDetails(
        IReadOnlyList<McpServerOptions> servers
    ) => [.. servers.Select(server => $"{server.Name} — {Truncate(Command(server))}")];

    /// <summary>
    /// The interactive per-server approval question; its one detail line is
    /// <see cref="Command"/> — the full command, never truncated (the config-file prompt's
    /// truncation does not apply to what is being approved). The question names that the server
    /// sees the environment: a stdio server inherits Lunate's whole shell environment (issue
    /// #449), so approval is what stands between a server and the provider keys in it.
    /// </summary>
    internal static string McpServerQuestion(McpServerOptions server) =>
        $"Run project MCP server '{server.Name}' from '{RealPath(server.Source ?? string.Empty)}' — it sees your shell environment";

    internal static string Command(McpServerOptions server) =>
        server.Arguments.Count == 0
            ? server.Command
            : server.Command + " " + string.Join(' ', server.Arguments);

    internal static string Truncate(string text) =>
        text.Length <= MaxCommandLength ? text : text[..MaxCommandLength] + "…";
}
