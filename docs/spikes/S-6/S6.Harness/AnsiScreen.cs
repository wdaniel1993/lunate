using System.Text;

namespace S6.Harness;

/// <summary>
/// Minimal ANSI/VT screen model for spike assertions: applies captured output
/// to a fixed character grid (cursor moves, erase, save/restore, SGR ignored).
/// It models single-cell characters only; the scenario text is ASCII.
/// </summary>
public sealed class AnsiScreen
{
    private readonly int _width;
    private readonly int _height;
    private readonly char[] _cells;

    private int _row;
    private int _col;
    private int _savedRow;
    private int _savedCol;
    private bool _hasSaved;

    public AnsiScreen(int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _cells = new char[_width * _height];
        ClearAll();
    }

    public int CursorRow => _row;

    public int CursorCol => _col;

    public void Apply(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i++];
            switch (ch)
            {
                case '\x1b':
                    i = ApplyEscape(text, i);
                    continue;
                case '\r':
                    _col = 0;
                    continue;
                case '\n':
                    _row++;
                    if (_row >= _height)
                    {
                        _row = _height - 1;
                    }
                    _col = 0;
                    continue;
                case '\b':
                    _col = Math.Max(0, _col - 1);
                    continue;
                case '\t':
                    _col = Math.Min(_width - 1, ((_col / 8) + 1) * 8);
                    continue;
                default:
                    Write(ch);
                    continue;
            }
        }
    }

    public string GetText()
    {
        var lines = new string[_height];
        for (var y = 0; y < _height; y++)
        {
            lines[y] = new string(_cells, y * _width, _width).TrimEnd();
        }

        var last = _height - 1;
        while (last >= 0 && lines[last].Length == 0)
        {
            last--;
        }

        return last < 0 ? string.Empty : string.Join('\n', lines.Take(last + 1));
    }

    public string GetLine(int row) =>
        row >= 0 && row < _height ? new string(_cells, row * _width, _width).TrimEnd() : string.Empty;

    public IReadOnlyList<string> GetLines() => [.. GetText().Split('\n')];

    private void ClearAll() => Array.Fill(_cells, ' ');

    private void Write(char ch)
    {
        if (_row < 0 || _row >= _height)
        {
            return;
        }

        if (_col >= 0 && _col < _width)
        {
            _cells[(_row * _width) + _col] = ch;
        }

        _col++;
        if (_col >= _width)
        {
            _col = _width;
        }
    }

    private int ApplyEscape(string text, int i)
    {
        if (i >= text.Length)
        {
            return i;
        }

        var next = text[i];
        if (next == '[')
        {
            return ApplyCsi(text, i + 1);
        }

        if (next == ']')
        {
            return SkipOsc(text, i + 1);
        }

        i++;
        switch (next)
        {
            case '7':
                Save();
                break;
            case '8':
                Restore();
                break;
        }

        return i;
    }

    private int ApplyCsi(string text, int i)
    {
        // Optional private-mode prefix (?); collected but ignored.
        if (i < text.Length && text[i] == '?')
        {
            i++;
        }

        var parameters = new List<int>();
        var current = 0;
        var hasNumber = false;
        while (i < text.Length)
        {
            var ch = text[i++];
            if (ch >= '0' && ch <= '9')
            {
                current = (current * 10) + (ch - '0');
                hasNumber = true;
                continue;
            }

            if (ch == ';')
            {
                parameters.Add(hasNumber ? current : 0);
                current = 0;
                hasNumber = false;
                continue;
            }

            parameters.Add(hasNumber ? current : 0);
            Execute(ch, parameters);
            return i;
        }

        return i;
    }

    private static int SkipOsc(string text, int i)
    {
        while (i < text.Length)
        {
            if (text[i] == '\a')
            {
                return i + 1;
            }

            if (text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '\\')
            {
                return i + 2;
            }

            i++;
        }

        return i;
    }

    private void Execute(char final, List<int> p)
    {
        int First(int fallback) => p.Count > 0 && p[0] > 0 ? p[0] : fallback;

        switch (final)
        {
            case 'A':
                _row = Math.Max(0, _row - First(1));
                break;
            case 'B':
                _row = Math.Min(_height - 1, _row + First(1));
                break;
            case 'C':
                _col = Math.Min(_width - 1, _col + First(1));
                break;
            case 'D':
                _col = Math.Max(0, _col - First(1));
                break;
            case 'E':
                _row = Math.Min(_height - 1, _row + First(1));
                _col = 0;
                break;
            case 'F':
                _row = Math.Max(0, _row - First(1));
                _col = 0;
                break;
            case 'G':
                _col = Math.Clamp(First(1) - 1, 0, _width - 1);
                break;
            case 'd':
                _row = Math.Clamp(First(1) - 1, 0, _height - 1);
                break;
            case 'H':
            case 'f':
                _row = Math.Clamp((p.Count > 0 && p[0] > 0 ? p[0] : 1) - 1, 0, _height - 1);
                _col = Math.Clamp((p.Count > 1 && p[1] > 0 ? p[1] : 1) - 1, 0, _width - 1);
                break;
            case 'K':
            {
                var mode = p.Count > 0 ? p[0] : 0;
                var start = mode switch
                {
                    1 => 0,
                    2 => 0,
                    _ => _col,
                };
                var end = mode switch
                {
                    1 => _col,
                    2 => _width - 1,
                    _ => _width - 1,
                };
                for (var x = Math.Max(0, start); x <= Math.Min(_width - 1, end); x++)
                {
                    _cells[(_row * _width) + x] = ' ';
                }

                break;
            }
            case 'J':
            {
                var mode = p.Count > 0 ? p[0] : 0;
                switch (mode)
                {
                    case 0:
                        for (var x = _col; x < _width; x++)
                        {
                            _cells[(_row * _width) + x] = ' ';
                        }
                        for (var y = _row + 1; y < _height; y++)
                        {
                            Array.Fill(_cells, ' ', y * _width, _width);
                        }
                        break;
                    case 1:
                        for (var x = 0; x <= _col && x < _width; x++)
                        {
                            _cells[(_row * _width) + x] = ' ';
                        }
                        for (var y = 0; y < _row; y++)
                        {
                            Array.Fill(_cells, ' ', y * _width, _width);
                        }
                        break;
                    default:
                        ClearAll();
                        break;
                }

                break;
            }
            case 's':
                Save();
                break;
            case 'u':
                Restore();
                break;
        }
    }

    private void Save()
    {
        _savedRow = _row;
        _savedCol = _col;
        _hasSaved = true;
    }

    private void Restore()
    {
        if (!_hasSaved)
        {
            return;
        }

        _row = _savedRow;
        _col = _savedCol;
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(GetText());
        return builder.ToString();
    }
}
