using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>One line of a session file: a header, a message, a compaction or a model change.</summary>
public abstract record SessionEntry(string Id);

/// <summary>The header line: format schema, session id, working directory, creation time, MEAI version.</summary>
public sealed record SessionHeaderEntry(
    string Id,
    int Schema,
    string Cwd,
    DateTimeOffset Created,
    string Meai
) : SessionEntry(Id);

/// <summary>A conversation message with the model that produced it and its usage when known.</summary>
public sealed record SessionMessageEntry(
    string Id,
    string? ParentId,
    DateTimeOffset Timestamp,
    ChatMessage Message,
    string? Model,
    SessionUsage? Usage
) : SessionEntry(Id);

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

/// <summary>Token usage recorded on an assistant entry.</summary>
public sealed record SessionUsage(int Input, int Output);
