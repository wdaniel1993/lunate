namespace S5.Harness;

public static class Scenario
{
    /// <summary>~30 fps redraw cap for the streaming tail.</summary>
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);

    public const int InitialWidth = 80;

    public const int InitialHeight = 24;

    public const string Model = "gpt-5.1-codex";
}
