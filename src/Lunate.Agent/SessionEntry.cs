using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>One line of a session file: a header, a message, a store entry or an extension entry.</summary>
public abstract record SessionEntry(string Id);

/// <summary>The header line: format schema, session id, working directory, creation time, MEAI version.</summary>
public sealed record SessionHeaderEntry(
    string Id,
    int Schema,
    string Cwd,
    DateTimeOffset Created,
    string Meai,
    string? Repo = null,
    string? Worktree = null
) : SessionEntry(Id)
{
    /// <summary>
    /// The session and entry a <c>/fork</c> or <c>/clone</c> came from (schema 3); null for a
    /// session that started on its own. Internal: provenance is written and re-read by the store,
    /// and this change adds no public surface.
    /// </summary>
    internal SessionOrigin? Origin { get; init; }

    /// <summary>Whether this file is a fork or clone of another session (schema 3).</summary>
    internal bool Branched { get; init; }
}

/// <summary>Where a session file came from: the source session id and the entry it was taken at.</summary>
internal sealed record SessionOrigin(string SessionId, string EntryId);

/// <summary>A conversation message with the model that produced it and its usage when known.</summary>
public sealed record SessionMessageEntry(
    string Id,
    string? ParentId,
    DateTimeOffset Timestamp,
    ChatMessage Message,
    string? Model,
    SessionUsage? Usage
) : SessionEntry(Id)
{
    /// <summary>
    /// The original message JSON when the parsed <see cref="ChatMessage"/> does not re-serialize to
    /// the same bytes (content the core cannot round-trip); null serializes the live message.
    /// </summary>
    public string? RawMessageJson { get; init; }

    /// <summary>
    /// The skill invocation this user turn carries (name and base directory), so session files stay
    /// self-describing; null for ordinary turns.
    /// </summary>
    public SessionSkillMetadata? Skill { get; init; }
}

/// <summary>The skill metadata recorded on an invoked skill's user turn.</summary>
public sealed record SessionSkillMetadata(string Name, string Dir);

/// <summary>A summary that replaces the listed earlier entries in future requests.</summary>
public sealed record SessionCompactionEntry(
    string Id,
    string? ParentId,
    DateTimeOffset Timestamp,
    string Summary,
    IReadOnlyList<string> Replaces
) : SessionEntry(Id);

/// <summary>A model change recorded at this point of the session.</summary>
public sealed record SessionModelChangeEntry(
    string Id,
    string? ParentId,
    DateTimeOffset Timestamp,
    string Model
) : SessionEntry(Id);

/// <summary>The active-tool set at this point of the session, in exposure order.</summary>
public sealed record SessionActiveToolsEntry(
    string Id,
    string? ParentId,
    DateTimeOffset Timestamp,
    IReadOnlyList<string> Tools
) : SessionEntry(Id);

/// <summary>A system-prompt-section change for deterministic replay.</summary>
public sealed record SessionPromptSectionEntry(
    string Id,
    string? ParentId,
    DateTimeOffset Timestamp,
    string Section,
    string Text
) : SessionEntry(Id);

/// <summary>A link to a child session run.</summary>
public sealed record SessionChildSessionEntry(
    string Id,
    string? ParentId,
    DateTimeOffset Timestamp,
    string ChildSessionId,
    string RunId
) : SessionEntry(Id);

/// <summary>Bounded records of the nested calls made by one top-level tool call.</summary>
public sealed record SessionNestedCallsEntry(
    string Id,
    string? ParentId,
    DateTimeOffset Timestamp,
    string CallId,
    IReadOnlyList<SessionNestedCall> Calls
) : SessionEntry(Id);

/// <summary>
/// A move of the session's active leaf (schema 3). Written only when the leaf moves backwards; the
/// most recently appended one wins, and the leaf is otherwise the most recently appended entry.
/// The entry itself is on no path: it is a marker, so it never reaches a request.
/// </summary>
internal sealed record SessionActiveLeafEntry(
    string Id,
    string? ParentId,
    DateTimeOffset Timestamp,
    string LeafId
) : SessionEntry(Id);

/// <summary>
/// A summary of the branch that was left, written as the first entry after the leaf moved to
/// (schema 3). Ordinary context on the destination path, never a compaction: it carries no
/// <c>replaces</c> list and replaces nothing.
/// </summary>
internal sealed record SessionBranchSummaryEntry(
    string Id,
    string? ParentId,
    DateTimeOffset Timestamp,
    string FromLeafId,
    string Summary
) : SessionEntry(Id);

/// <summary>One nested tool call: name, capped arguments, status and duration; never its result.</summary>
public sealed record SessionNestedCall(string Name, string Args, string Status, int DurationMs);

/// <summary>An extension-owned entry whose opaque payload is preserved as raw JSON text.</summary>
public sealed record SessionExtensionEntry(
    string Id,
    string? ParentId,
    DateTimeOffset Timestamp,
    string ExtensionType,
    string PayloadJson
) : SessionEntry(Id)
{
    /// <summary>The payload parsed on access; the write path always uses <see cref="PayloadJson"/>.</summary>
    public JsonNode? Payload => JsonNode.Parse(PayloadJson);
}

/// <summary>
/// Token usage recorded on an assistant entry. <see cref="CacheRead"/> and
/// <see cref="CacheWrite"/> are the provider's cache lanes; null means unknown (a schema 1 or 2
/// entry, or a provider that reports no cache counts). Only positive cache counts are persisted.
/// </summary>
public sealed record SessionUsage(
    int Input,
    int Output,
    int? CacheRead = null,
    int? CacheWrite = null
)
{
    /// <summary>The model usage attribution added in session schema 4; null for schemas 1–3.</summary>
    public ModelUsageAttribution? Attribution { get; init; }
}
