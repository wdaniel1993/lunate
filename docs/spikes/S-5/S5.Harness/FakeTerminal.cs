namespace S5.Harness;

public sealed record TerminalFrame(int Index, string[] Lines);

/// <summary>
/// In-memory terminal. Finished blocks (Spectre-rendered) land in
/// <see cref="Scrollback"/>; the live area is painted with <see cref="Render"/>
/// and every paint is recorded for frame-cap and spinner assertions.
/// </summary>
public sealed class FakeTerminal
{
    private string[] _liveArea = [];

    public FakeTerminal(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public List<string> Scrollback { get; } = [];

    public List<TerminalFrame> Frames { get; } = [];

    public event Action<int, int>? Resized;

    public IReadOnlyList<string> LiveArea => _liveArea;

    public string LiveText => string.Join('\n', _liveArea);

    public void WriteBlock(string text) => Scrollback.Add(text);

    public void Render(IReadOnlyList<string> lines)
    {
        _liveArea = [.. lines];
        Frames.Add(new TerminalFrame(Frames.Count, _liveArea));
    }

    public void Resize(int width, int height)
    {
        Width = width;
        Height = height;
        Resized?.Invoke(width, height);
    }
}
