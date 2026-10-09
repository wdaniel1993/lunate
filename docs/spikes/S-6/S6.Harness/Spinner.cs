namespace S6.Harness;

public static class Spinner
{
    public static readonly char[] Frames = ['|', '/', '-', '\\'];

    public static string? Glyph(bool busy, int frameNumber) =>
        busy ? Frames[frameNumber % Frames.Length].ToString() : null;
}
