namespace Lunate.Tui;

/// <summary>The resolved bash-guard fact shared by the context command and welcome header.</summary>
internal sealed record BashGuardStatus(string Mechanism, string ProbeState, bool Enabled)
{
    internal static BashGuardStatus Disabled { get; } = new("none", "not run", Enabled: false);

    internal string ContextText =>
        Enabled
            ? $"bash guard: {Mechanism}; probe: {ProbeState}"
            : "bash guard: none; probe: not run; disabled";

    internal string HeaderText(bool unicode) =>
        Enabled
            ? $"bash guard {Mechanism}{Separator(unicode)}probe {ProbeState}"
            : $"bash guard disabled{Separator(unicode)}none{Separator(unicode)}probe not run";

    private static string Separator(bool unicode) => unicode ? " · " : " - ";
}
