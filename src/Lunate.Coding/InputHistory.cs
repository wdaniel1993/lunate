using System.Text.Json;

namespace Lunate.Coding;

/// <summary>
/// The input-line history behind Up/Down: one JSON-encoded string per line in
/// <c>~/.lunate/history</c>, so multiline entries stay one line on disk; corrupt lines are skipped
/// on load. Navigation keeps a cursor: <c>Up</c> walks to older entries, <c>Down</c> back to newer
/// ones and reports false when the walk leaves the history - the caller restores its draft then.
/// </summary>
internal sealed class InputHistory
{
    private readonly string _path;
    private readonly List<string> _entries = [];
    private int _cursor;

    public InputHistory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        Load();
    }

    /// <summary>The default history file under the user profile.</summary>
    public static string DefaultPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lunate",
            "history"
        );

    /// <summary>The stored entries, oldest first.</summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Appends one submitted text; blank input is ignored but resets navigation.</summary>
    public void Add(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            _cursor = _entries.Count;
            return;
        }

        _entries.Add(text);
        _cursor = _entries.Count;
        Append(text);
    }

    /// <summary>Moves to the older entry; false at the oldest entry (or an empty history).</summary>
    public bool TryPrevious(out string text)
    {
        if (_cursor == 0 || _entries.Count == 0)
        {
            _cursor = 0;
            text = _entries.Count == 0 ? string.Empty : _entries[0];
            return false;
        }

        _cursor--;
        text = _entries[_cursor];
        return true;
    }

    /// <summary>Moves to the newer entry; false when the walk leaves the history.</summary>
    public bool TryNext(out string text)
    {
        if (_cursor >= _entries.Count)
        {
            text = string.Empty;
            return false;
        }

        _cursor++;
        if (_cursor == _entries.Count)
        {
            text = string.Empty;
            return false;
        }

        text = _entries[_cursor];
        return true;
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        foreach (string line in File.ReadLines(_path))
        {
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<string>(line) is { } text)
                {
                    _entries.Add(text);
                }
            }
            catch (JsonException)
            {
                // A corrupt line is skipped; the rest of the history stays usable.
            }
        }

        _cursor = _entries.Count;
    }

    private void Append(string text)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(_path, JsonSerializer.Serialize(text) + "\n");
    }
}
