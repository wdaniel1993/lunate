namespace Lunate.Coding;

/// <summary>
/// The in-memory history behind Up/Down in bash mode. It mirrors the prompt history's navigation
/// but never touches the history file: bash lines live for this session only.
/// </summary>
internal sealed class BashHistory
{
    private readonly List<string> _entries = [];
    private int _cursor;

    /// <summary>The stored commands, oldest first.</summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Appends one submitted command; blank input is ignored but resets navigation.</summary>
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
    }

    /// <summary>Starts a new navigation at the newest entry.</summary>
    public void Begin() => _cursor = _entries.Count;

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
}
