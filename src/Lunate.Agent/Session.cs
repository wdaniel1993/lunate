using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// One history item: a message, the session entry id it came from (null for a summary) and the
/// model id recorded on that entry (null when unknown).
/// </summary>
internal sealed record SessionHistoryItem(ChatMessage Message, string? EntryId, string? Model);

/// <summary>
/// An append-only JSONL session. Create one, append messages, compaction and model changes; load it
/// to resume. The store assigns entry ids, the parent chain and the timestamps, so callers pass
/// payloads only. The byte format is frozen by the golden tests in tests/fixtures/sessions.
/// Sessions assume a single writer (one harness or process per file); concurrent writers are not
/// supported.
/// </summary>
public sealed partial class Session
{
    private const int MaxNestedCalls = 32;
    private const int MaxNestedArgsLength = 200;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TimeProvider _timeProvider;
    private readonly List<SessionEntry> _entries = [];
    private SessionHeaderEntry? _header;
    private SessionImages? _images;

    internal Session(string path, string sessionId, TimeProvider timeProvider)
    {
        Path = path;
        SessionId = sessionId;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// The session's header line: schema, id, working directory, creation time, MEAI version and
    /// the optional repository identity. Used to mirror a parent's header onto a child session.
    /// </summary>
    internal SessionHeaderEntry Header =>
        _header
        ?? throw new InvalidOperationException(
            $"Session '{SessionId}' has no header; only created or loaded sessions can be used."
        );

    /// <summary>The session id, also the header's id.</summary>
    public string SessionId { get; }

    /// <summary>The session file path.</summary>
    public string Path { get; }

    /// <summary>
    /// The header's format schema (1, 2 or 3), so consumers can tell replay formats apart: schema 3
    /// may carry both cache token lanes and image reference parts, older schemas never do.
    /// </summary>
    public int Schema => Header.Schema;

    /// <summary>The conversation entries in append order; the header entry is not included.</summary>
    public IReadOnlyList<SessionEntry> Entries => _entries;

    /// <summary>The content-addressed image store beside this session file.</summary>
    internal SessionImages Images => _images ??= SessionImages.ForSession(this);

    /// <summary>Creates a session file and writes its header with the optional repository identity.</summary>
    public static Session Create(
        string path,
        string cwd,
        string? repo = null,
        string? worktree = null
    ) => Create(path, cwd, repo, worktree, TimeProvider.System);

    internal static Session Create(string path, string cwd, TimeProvider timeProvider) =>
        Create(path, cwd, repo: null, worktree: null, timeProvider);

    internal static Session Create(
        string path,
        string cwd,
        string? repo,
        string? worktree,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        DateTimeOffset now = timeProvider.GetUtcNow();
        return CreateCore(path, cwd, repo, worktree, NewSessionId(now), now, timeProvider);
    }

    /// <summary>Creates a session with an explicit id (tests and imports), keeping the id path-safe.</summary>
    internal static Session Create(
        string path,
        string cwd,
        string sessionId,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return CreateCore(
            path,
            cwd,
            repo: null,
            worktree: null,
            sessionId,
            timeProvider.GetUtcNow(),
            timeProvider
        );
    }

    private static Session CreateCore(
        string path,
        string cwd,
        string? repo,
        string? worktree,
        string sessionId,
        DateTimeOffset now,
        TimeProvider timeProvider
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _ = SessionPaths.SessionFileName(sessionId);

        var session = new Session(path, sessionId, timeProvider);
        var header = new SessionHeaderEntry(
            session.SessionId,
            SessionFormat.SchemaVersion,
            cwd,
            now,
            SessionFormat.MeaiVersion,
            repo,
            worktree
        );
        session.WriteHeader(header);
        session._header = header;
        session._images = SessionImages.ForSession(session);
        session._images.ResetForNewSession();
        return session;
    }

    /// <summary>Loads a session file, validating the header schema with an actionable error.</summary>
    public static Session Load(string path) => Load(path, TimeProvider.System);

    internal static Session Load(string path, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(timeProvider);

        string[] lines = File.ReadAllText(path).Split('\n');
        int count = lines.Length;
        if (count > 0 && lines[^1].Length == 0)
        {
            count--;
        }

        if (count == 0)
        {
            throw new InvalidDataException(
                $"Session file '{path}' is empty; expected a header line with schema {SessionFormat.SchemaVersion}."
            );
        }

        SessionHeaderEntry header =
            ParseLine(lines[0], path, 1) as SessionHeaderEntry
            ?? throw new InvalidDataException(
                $"Session file '{path}' line 1 is not a header; expected a 'header' entry with schema {SessionFormat.SchemaVersion}."
            );

        if (header.Schema is not (1 or 2 or SessionFormat.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Session file '{path}' declares schema '{header.Schema}' but this build supports schema 1, 2 and {SessionFormat.SchemaVersion}."
            );
        }

        var session = new Session(path, header.Id, timeProvider) { _header = header };
        for (int index = 1; index < count; index++)
        {
            SessionEntry entry = ParseLine(lines[index], path, index + 1);
            if (entry is SessionHeaderEntry)
            {
                throw new InvalidDataException(
                    $"Session file '{path}' line {index + 1} is an unexpected second header."
                );
            }

            session._entries.Add(entry);
        }

        try
        {
            session._images = SessionImages.ForSession(session);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"Session file '{path}' has an invalid session id '{header.Id}': {exception.Message}",
                exception
            );
        }

        session._images.Sweep(
            SessionImages.ReferencedDigests(session._entries),
            timeProvider.GetUtcNow()
        );
        return session;
    }

    /// <summary>Appends a message; the model and usage are recorded when known.</summary>
    public void AppendMessage(ChatMessage message, string? model = null, SessionUsage? usage = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        AddEntry(
            new SessionMessageEntry(
                NextEntryId(),
                LastEntryId,
                _timeProvider.GetUtcNow(),
                message,
                model,
                usage
            )
        );
    }

    /// <summary>
    /// Appends a user message carrying its skill invocation metadata (name, base directory); the
    /// model and usage are recorded when known.
    /// </summary>
    public void AppendSkillMessage(
        ChatMessage message,
        SessionSkillMetadata skill,
        string? model = null,
        SessionUsage? usage = null
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(skill);
        AddEntry(
            new SessionMessageEntry(
                NextEntryId(),
                LastEntryId,
                _timeProvider.GetUtcNow(),
                message,
                model,
                usage
            )
            {
                Skill = skill,
            }
        );
    }

    /// <summary>Appends a compaction entry that replaces the listed earlier entries.</summary>
    public void AppendCompaction(string summary, IReadOnlyList<string> replaces)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(replaces);
        AddEntry(
            new SessionCompactionEntry(
                NextEntryId(),
                LastEntryId,
                _timeProvider.GetUtcNow(),
                summary,
                [.. replaces]
            )
        );
    }

    /// <summary>Appends a model change.</summary>
    public void AppendModelChange(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        AddEntry(
            new SessionModelChangeEntry(
                NextEntryId(),
                LastEntryId,
                _timeProvider.GetUtcNow(),
                model
            )
        );
    }

    /// <summary>Appends the active-tool set in exposure order.</summary>
    public void AppendActiveTools(IReadOnlyList<string> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        AddEntry(
            new SessionActiveToolsEntry(
                NextEntryId(),
                LastEntryId,
                _timeProvider.GetUtcNow(),
                [.. tools]
            )
        );
    }

    /// <summary>Appends a system-prompt-section change for deterministic replay.</summary>
    public void AppendPromptSection(string section, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentNullException.ThrowIfNull(text);
        AddEntry(
            new SessionPromptSectionEntry(
                NextEntryId(),
                LastEntryId,
                _timeProvider.GetUtcNow(),
                section,
                text
            )
        );
    }

    /// <summary>Appends a link to a child session run.</summary>
    public void AppendChildSession(string childSessionId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        AddEntry(
            new SessionChildSessionEntry(
                NextEntryId(),
                LastEntryId,
                _timeProvider.GetUtcNow(),
                childSessionId,
                runId
            )
        );
    }

    /// <summary>
    /// Appends one bounded nested-call record for a top-level tool call: names, capped arguments,
    /// status and duration only, never results. At most 32 calls are recorded; arguments longer
    /// than 200 characters are truncated with a trailing marker.
    /// </summary>
    public void AppendNestedCalls(string callId, IReadOnlyList<SessionNestedCall> calls)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentNullException.ThrowIfNull(calls);
        List<SessionNestedCall> bounded = [];
        foreach (SessionNestedCall call in calls)
        {
            if (call.Status is not ("ok" or "error" or "cancelled"))
            {
                throw new ArgumentException(
                    $"Nested call status '{call.Status}' must be 'ok', 'error' or 'cancelled'.",
                    nameof(calls)
                );
            }

            if (bounded.Count == MaxNestedCalls)
            {
                continue;
            }

            bounded.Add(
                call.Args.Length > MaxNestedArgsLength
                    ? call with
                    {
                        Args = call.Args[..MaxNestedArgsLength] + "…",
                    }
                    : call
            );
        }

        AddEntry(
            new SessionNestedCallsEntry(
                NextEntryId(),
                LastEntryId,
                _timeProvider.GetUtcNow(),
                callId,
                bounded
            )
        );
    }

    /// <summary>Appends an extension entry; the payload is written verbatim as JSON text.</summary>
    public void AppendExtension(string extensionType, string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        if (!SessionFormat.IsExtensionType(extensionType))
        {
            throw new ArgumentException(
                $"Extension type '{extensionType}' must be of the form 'ext/<extension-id>/<type>'.",
                nameof(extensionType)
            );
        }

        try
        {
            _ = JsonNode.Parse(payloadJson);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                $"Extension payload is not valid JSON: {exception.Message}",
                nameof(payloadJson),
                exception
            );
        }

        AddEntry(
            new SessionExtensionEntry(
                NextEntryId(),
                LastEntryId,
                _timeProvider.GetUtcNow(),
                extensionType,
                payloadJson
            )
        );
    }

    /// <summary>The message entries in append order, ready to seed a harness.</summary>
    public List<ChatMessage> ToHistory() => [.. HistoryItems().Select(item => item.Message)];

    /// <summary>
    /// The history items for seeding a harness: the last compaction's summary message (when the
    /// session was compacted) followed by every message entry the summary does not replace, in
    /// append order. Each item carries the entry id it came from; the summary has none.
    /// </summary>
    internal List<SessionHistoryItem> HistoryItems()
    {
        SessionCompactionEntry? compaction = _entries
            .OfType<SessionCompactionEntry>()
            .LastOrDefault();
        HashSet<string>? replaced = compaction is null
            ? null
            : new HashSet<string>(compaction.Replaces, StringComparer.Ordinal);
        List<SessionHistoryItem> items = [];
        if (compaction is not null)
        {
            items.Add(
                new SessionHistoryItem(
                    new ChatMessage(ChatRole.Assistant, compaction.Summary),
                    null,
                    null
                )
            );
        }

        foreach (SessionMessageEntry entry in _entries.OfType<SessionMessageEntry>())
        {
            if (replaced is null || !replaced.Contains(entry.Id))
            {
                items.Add(new SessionHistoryItem(entry.Message, entry.Id, entry.Model));
            }
        }

        return items;
    }

    private string? LastEntryId => _entries.Count == 0 ? null : _entries[^1].Id;

    private string NextEntryId() => $"e_{_entries.Count + 1:D2}";

    private static string NewSessionId(DateTimeOffset now) =>
        "s_"
        + now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
        + "-"
        + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    private void WriteHeader(SessionHeaderEntry header)
    {
        string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(Path, SessionFormat.Serialize(header) + "\n", Utf8NoBom);
    }

    private void AddEntry(SessionEntry entry)
    {
        _entries.Add(entry);
        File.AppendAllText(Path, SessionFormat.Serialize(entry) + "\n", Utf8NoBom);
    }

    private static SessionEntry ParseLine(string line, string path, int lineNumber)
    {
        try
        {
            return SessionFormat.Parse(line);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                $"Session file '{path}' line {lineNumber} is invalid: {exception.Message}",
                exception
            );
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Session file '{path}' line {lineNumber} is invalid: {exception.Message}",
                exception
            );
        }
        catch (ArgumentException exception)
        {
            // A content part's constructor rejects hostile values (for example an image digest);
            // report it like every other invalid line, naming the file and line.
            throw new InvalidDataException(
                $"Session file '{path}' line {lineNumber} is invalid: {exception.Message}",
                exception
            );
        }
    }
}
