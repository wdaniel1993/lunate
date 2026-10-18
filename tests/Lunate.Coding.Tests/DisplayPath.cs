namespace Lunate.Coding.Tests;

/// <summary>
/// The product's display form of a path, mirrored from <c>InteractiveSession.DisplayDirectory</c>:
/// a path under the user's home is <c>~</c>-shortened. Tests must compute the expected form instead
/// of the canonical path: the test temp directory is under the home on Windows CI (and can be moved
/// under it elsewhere with <c>TMPDIR</c>).
/// </summary>
internal static class DisplayPath
{
    public static string For(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }
}
