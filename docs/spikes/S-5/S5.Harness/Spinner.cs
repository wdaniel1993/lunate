namespace S5.Harness;

public static class Spinner
{
    private static readonly char[] Frames = ['|', '/', '-', '\\'];

    public static string? Glyph(bool busy, int frameNumber) =>
        busy ? Frames[frameNumber % Frames.Length].ToString() : null;

    public static bool IsGlyphLine(string line) =>
        line.Length >= 2 && line[1] == ' ' && Frames.Contains(line[0]);
}
