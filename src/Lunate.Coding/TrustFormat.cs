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
            _ => "extensions",
        };
        return $"{kind} — {record.WorktreePath} {ShortHash(record.ContentHash)}";
    }

    internal static string ShortHash(string hash) =>
        hash.Length > ShortHashLength ? hash[..ShortHashLength] : hash;

    /// <summary>The interactive extension question; a re-prompt says the files changed.</summary>
    internal static string ExtensionQuestion(ExtensionDescriptor descriptor, bool filesChanged) =>
        filesChanged
            ? $"Load project extension '{descriptor.Id}' {descriptor.Version} from {descriptor.Directory} — its files changed since this repository trusted it. Load the new version"
            : $"Load project extension '{descriptor.Id}' {descriptor.Version} from {descriptor.Directory}";

    internal static string McpQuestionTitle(string configPath, int serverCount) =>
        FormattableString.Invariant(
            $"Start {serverCount} {(serverCount == 1 ? "server" : "servers")} from project mcp.json '{configPath}'"
        );

    internal static IReadOnlyList<string> McpQuestionDetails(
        IReadOnlyList<McpServerOptions> servers
    ) => [.. servers.Select(server => $"{server.Name} — {Truncate(Command(server))}")];

    /// <summary>
    /// The interactive per-server approval question; its one detail line is
    /// <see cref="Command"/> — the full command, never truncated (the config-file prompt's
    /// truncation does not apply to what is being approved).
    /// </summary>
    internal static string McpServerQuestion(McpServerOptions server) =>
        $"Run project MCP server '{server.Name}' from '{server.Source}'";

    internal static string Command(McpServerOptions server) =>
        server.Arguments.Count == 0
            ? server.Command
            : server.Command + " " + string.Join(' ', server.Arguments);

    internal static string Truncate(string text) =>
        text.Length <= MaxCommandLength ? text : text[..MaxCommandLength] + "…";
}
