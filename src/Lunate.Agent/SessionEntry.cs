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
) : SessionEntry(Id);

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
}

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

/// <summary>Token usage recorded on an assistant entry.</summary>
public sealed record SessionUsage(int Input, int Output);
