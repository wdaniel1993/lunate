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
        string privileges = descriptor.Manifest.Runtime is not null
            ? " — it runs with your user privileges"
            : "";
        string what =
            descriptor.Scope is ExtensionScope.Global
                ? $"extension '{descriptor.Id}' {descriptor.Version} installed in '{descriptor.Directory}'"
                : $"project extension '{descriptor.Id}' {descriptor.Version} from {descriptor.Directory}";
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
    /// The question's detail lines: the one provenance line an installed extension carries (design
    /// D7), so the prompt, the trust report and the tests read one wording.
    /// </summary>
    internal static IReadOnlyList<string> ProvenanceLine(string? fromPackage) =>
        fromPackage is { Length: > 0 } ? [fromPackage] : [];

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
