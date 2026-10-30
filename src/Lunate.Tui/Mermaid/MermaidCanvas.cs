namespace Lunate.Tui;

/// <summary>The glyphs one diagram is painted with: Unicode box drawing, or the ASCII stand-ins.</summary>
internal sealed record MermaidGlyphs(
    char CornerTopLeft,
    char CornerTopRight,
    char CornerBottomLeft,
    char CornerBottomRight,
    char Vertical,
    char Horizontal,
    char CornerLeftDown,
    char CornerLeftUp,
    char CornerRightDown,
    char CornerRightUp,
    char HeadDown,
    char HeadRight,
    char HeadLeft,
    char DashedHorizontal,
    char DashedVertical,
    char BracedTop,
    char BracedBottom,
    char BracedLeft,
    char BracedRight,
    char Ellipsis
)
{
    internal static readonly MermaidGlyphs UnicodeGlyphs = new(
        CornerTopLeft: '╭',
        CornerTopRight: '╮',
        CornerBottomLeft: '╰',
        CornerBottomRight: '╯',
        Vertical: '│',
        Horizontal: '─',
        CornerLeftDown: '└',
        CornerLeftUp: '┘',
        CornerRightDown: '┐',
        CornerRightUp: '┌',
        HeadDown: '▾',
        HeadRight: '▸',
        HeadLeft: '◂',
        DashedHorizontal: '┄',
        DashedVertical: '┆',
        BracedTop: '┬',
        BracedBottom: '┴',
        BracedLeft: '├',
        BracedRight: '┤',
        Ellipsis: '…'
    );

    internal static readonly MermaidGlyphs AsciiGlyphs = new(
        CornerTopLeft: '+',
        CornerTopRight: '+',
        CornerBottomLeft: '+',
        CornerBottomRight: '+',
        Vertical: '|',
        Horizontal: '-',
        CornerLeftDown: '+',
        CornerLeftUp: '+',
        CornerRightDown: '+',
        CornerRightUp: '+',
        HeadDown: 'v',
        HeadRight: '>',
        HeadLeft: '<',
        DashedHorizontal: '-',
        DashedVertical: '|',
        BracedTop: '+',
        BracedBottom: '+',
        BracedLeft: '+',
        BracedRight: '+',
        Ellipsis: '…'
    );
}

internal enum MermaidLabelTile
{
    Frame,
    Text,
}

/// <summary>
/// The character grid a Mermaid diagram is drawn on. Links are painted first, boxes and label
/// runs after them; a box's interior is erased as label cells, so a link shows behind, never
/// through, a node. Coordinates are columns one rune wide, so diagram construction must not
/// stretch a cell with a wide rune.
/// </summary>
internal sealed class MermaidCanvas
{
    private readonly MermaidGlyphs _glyphs;
    private readonly Dictionary<(int X, int Y), (char Glyph, MermaidLabelTile Tile)> _cells = [];
    private int _maxX;
    private int _maxY;

    public MermaidCanvas(bool unicode)
    {
        _glyphs = unicode ? MermaidGlyphs.UnicodeGlyphs : MermaidGlyphs.AsciiGlyphs;
    }

    public MermaidGlyphs Glyphs => _glyphs;

    public int Width => _maxX + 1;

    public int Height => _maxY + 1;

    public bool Occupied(int x, int y) => _cells.ContainsKey((x, y));

    /// <summary>A frame glyph that keeps whatever the same cell got from an earlier link.</summary>
    public void Link(int x, int y, char glyph)
    {
        Touch(x, y);
        _ = _cells.TryAdd((x, y), (glyph, MermaidLabelTile.Frame));
    }

    /// <summary>A frame cell that overwrites — arrowheads and corners own their cell.</summary>
    public void Force(int x, int y, char glyph)
    {
        Touch(x, y);
        _cells[(x, y)] = (glyph, MermaidLabelTile.Frame);
    }

    /// <summary>Label cells, spaces included: a label erases the frame it sits on.</summary>
    public void Text(int x, int y, string text)
    {
        Touch(x + Math.Max(0, text.Length - 1), y);
        _maxY = Math.Max(_maxY, y);
        for (var index = 0; index < text.Length; index++)
        {
            _cells[(x + index, y)] = (text[index], MermaidLabelTile.Text);
        }
    }

    private void Touch(int x, int y)
    {
        _maxX = Math.Max(_maxX, x);
        _maxY = Math.Max(_maxY, y);
    }

    /// <summary>
    /// A rounded box one cell of padding around the label lines, over the links: <paramref
    /// name="height"/> is the label rows plus two, and the interior is erased.
    /// </summary>
    public void Box(int x, int y, int width, int height, IReadOnlyList<string> labelLines)
    {
        for (var column = 1; column < width - 1; column++)
        {
            Force(x + column, y, _glyphs.Horizontal);
            Force(x + column, y + height - 1, _glyphs.Horizontal);
        }

        for (var row = 1; row < height - 1; row++)
        {
            Force(x, y + row, _glyphs.Vertical);
            Force(x + width - 1, y + row, _glyphs.Vertical);
            string text = labelLines.Count < row ? string.Empty : labelLines[row - 1];
            int printed = Math.Min(text.Length, Math.Max(0, width - 4));
            int pad = Math.Max(0, (width - 4 - printed) / 2);
            Text(x + 2, y + row, new string(' ', width - 4));
            Text(x + 2 + pad, y + row, text[..printed]);
        }

        Force(x, y, _glyphs.CornerTopLeft);
        Force(x + width - 1, y, _glyphs.CornerTopRight);
        Force(x, y + height - 1, _glyphs.CornerBottomLeft);
        Force(x + width - 1, y + height - 1, _glyphs.CornerBottomRight);
    }

    /// <summary>The grid as styled lines: label runs plain, frame runs and the blanks dim.</summary>
    public List<StyledLine> ToLines()
    {
        var lines = new List<StyledLine>();
        for (var y = 0; y < Height; y++)
        {
            var line = new StyledLine();
            var builder = new System.Text.StringBuilder();
            var runTile = MermaidLabelTile.Frame;
            void Flush()
            {
                if (builder.Length == 0)
                {
                    return;
                }

                line.Add(
                    builder.ToString(),
                    runTile == MermaidLabelTile.Text ? SpanStyle.Plain : SpanStyle.Dim
                );
                builder.Clear();
            }

            for (var x = 0; x < Width; x++)
            {
                if (_cells.TryGetValue((x, y), out (char Glyph, MermaidLabelTile Tile) cell))
                {
                    if (cell.Tile != runTile)
                    {
                        Flush();
                        runTile = cell.Tile;
                    }

                    builder.Append(cell.Glyph);
                }
                else
                {
                    Flush();
                    runTile = MermaidLabelTile.Frame;
                    builder.Append(' ');
                }
            }

            Flush();
            lines.Add(line);
        }

        return lines;
    }
}
