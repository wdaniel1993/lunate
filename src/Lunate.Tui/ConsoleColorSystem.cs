using Spectre.Console;

namespace Lunate.Tui;

/// <summary>
/// The production bridge from the detected <see cref="ColorSystemSupport"/> to the concrete
/// <see cref="ColorSystem"/> a surface paints with. Spectre re-detects <c>Detect</c> when a console
/// is created, so the capability helper's resolution is made explicit here once; every surface
/// (the live area's scrollback, the transcript viewer) creates its console through this bridge.
/// </summary>
internal static class ConsoleColorSystem
{
    /// <summary>
    /// The concrete colour system for a detected capability: the explicit depths map one-to-one;
    /// <c>Detect</c> — what <see cref="TerminalCapabilities"/> returns when neither
    /// <c>NO_COLOR</c> nor <c>COLORTERM</c> decided — is Spectre's 256-colour fallback.
    /// </summary>
    public static ColorSystem Resolve(ColorSystemSupport color) =>
        color switch
        {
            ColorSystemSupport.NoColors => ColorSystem.NoColors,
            ColorSystemSupport.Legacy => ColorSystem.Legacy,
            ColorSystemSupport.Standard => ColorSystem.Standard,
            ColorSystemSupport.EightBit => ColorSystem.EightBit,
            ColorSystemSupport.TrueColor => ColorSystem.TrueColor,
            _ => ColorSystem.EightBit,
        };

    /// <summary>The console a Lunate surface writes through: ANSI per the console, the resolved colour system, no interaction.</summary>
    public static IAnsiConsole Create(ColorSystemSupport color, TextWriter output, bool ansi = true)
    {
        ArgumentNullException.ThrowIfNull(output);
        return AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = ansi ? AnsiSupport.Yes : AnsiSupport.No,
                ColorSystem = (ColorSystemSupport)Resolve(color),
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(output),
            }
        );
    }
}
