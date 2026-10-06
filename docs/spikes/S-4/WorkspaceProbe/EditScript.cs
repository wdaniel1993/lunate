namespace Spike.WorkspaceProbe;

internal static class EditScript
{
    public const string ErrorMarker = "probe-error";

    private const string StartMarker = "// <" + ErrorMarker + ">";
    private const string EndMarker = "// </" + ErrorMarker + ">";

    private const string ErrorBlock =
        StartMarker
        + "\n"
        + "internal static class ProbeError\n"
        + "{\n"
        + "    public static int Broken() => \"not an int\";\n"
        + "}\n"
        + EndMarker
        + "\n";

    public static bool HasError(string text) =>
        text.Contains(StartMarker, StringComparison.Ordinal);

    public static string InjectError(string text) => text + ErrorBlock;

    public static string RemoveError(string text)
    {
        var start = text.IndexOf(StartMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return text;
        }

        var end = text.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return text;
        }

        var endOfLine = text.IndexOf('\n', end);
        var removeThrough = endOfLine < 0 ? text.Length : endOfLine + 1;
        return text.Remove(start, removeThrough - start);
    }

    public static string Toggle(string text) =>
        HasError(text) ? RemoveError(text) : InjectError(text);
}
