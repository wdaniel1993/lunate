using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// An append-only JSONL session. Create one, append messages, compaction and model changes; load it
/// to resume. The store assigns entry ids, the parent chain and the timestamps, so callers pass
/// payloads only. The byte format is frozen by the golden tests in tests/fixtures/sessions.
/// Sessions assume a single writer (one harness or process per file); concurrent writers are not
/// supported.
/// </summary>
public sealed class Session
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TimeProvider _timeProvider;
    private readonly List<SessionEntry> _entries = [];

    internal Session(string path, string sessionId, TimeProvider timeProvider)
    {
        Path = path;
        SessionId = sessionId;
        _timeProvider = timeProvider;
    }

    /// <summary>The session id, also the header's id.</summary>
    public string SessionId { get; }

    /// <summary>The session file path.</summary>
    public string Path { get; }

    /// <summary>The conversation entries in append order; the header entry is not included.</summary>
    public IReadOnlyList<SessionEntry> Entries => _entries;

    /// <summary>Creates a session file and writes its header.</summary>
    public static Session Create(string path, string cwd) => Create(path, cwd, TimeProvider.System);

    internal static Session Create(string path, string cwd, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);
        ArgumentNullException.ThrowIfNull(timeProvider);

        DateTimeOffset now = timeProvider.GetUtcNow();
        var session = new Session(path, NewSessionId(now), timeProvider);
        var header = new SessionHeaderEntry(
            session.SessionId,
            SessionFormat.SchemaVersion,
            cwd,
            now,
            SessionFormat.MeaiVersion
        );
        session.WriteHeader(header);
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

        if (header.Schema != SessionFormat.SchemaVersion)
        {
            throw new InvalidDataException(
                $"Session file '{path}' declares schema '{header.Schema}' but this build supports schema {SessionFormat.SchemaVersion}."
            );
        }

        var session = new Session(path, header.Id, timeProvider);
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

    /// <summary>The message entries in append order, ready to seed a harness.</summary>
    public List<ChatMessage> ToHistory() =>
        [.. _entries.OfType<SessionMessageEntry>().Select(entry => entry.Message)];

    private string? LastEntryId => _entries.Count == 0 ? null : _entries[^1].Id;

    private string NextEntryId() => $"e_{_entries.Count + 1:D2}";

    private static string NewSessionId(DateTimeOffset now) =>
        "s_"
        + now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
        + "-"
        + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(2));

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
    }
}
