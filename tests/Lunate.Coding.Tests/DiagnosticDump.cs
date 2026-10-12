namespace Lunate.Coding.Tests;

/// <summary>Temporary CI diagnostics: frames with escapes made visible.</summary>
internal static class DiagnosticDump
{
    public static string Dump(InteractiveSessionHost host) =>
        "LASTFRAME:\n"
        + Vis(host.LastFrame)
        + "\nFRAMES_TAIL:\n"
        + Vis(Tail(host.Frames, 3000))
        + "\nDIAG:\n"
        + Vis(Tail(host.Diagnostics, 1500));

    private static string Tail(string text, int length) =>
        text.Length > length ? text[^length..] : text;

    private static string Vis(string text) =>
        text.Replace("\u001b", "<E>").Replace("\r", "<R>").Replace("\n", "<N>\n");
}
