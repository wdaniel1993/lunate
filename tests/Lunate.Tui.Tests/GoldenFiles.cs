namespace Lunate.Tui.Tests;

internal static class GoldenFiles
{
    // Regenerate deliberately with
    //   LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests
    // then review the diff before committing.
    public static bool UpdateRequested =>
        Environment.GetEnvironmentVariable("LUNATE_TUI_UPDATE_GOLDENS") == "1";

    public static void AssertMatchesLines(string path, IReadOnlyList<string> actualLines)
    {
        if (UpdateRequested)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, actualLines);
        }

        Assert.Equal(File.ReadAllLines(path), actualLines);
    }

    public static void AssertMatchesText(string path, string actual)
    {
        if (UpdateRequested)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }

        Assert.Equal(File.ReadAllText(path), actual);
    }
}
