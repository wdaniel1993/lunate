using Spectre.Console;
using Spectre.Console.Rendering;

namespace S5.Harness;

/// <summary>
/// Finished blocks are rendered with Spectre.Console (the product plan), but
/// into a plain-text buffer because the spike has no real terminal.
/// </summary>
public static class SpectreBlocks
{
    public static string ToolResult(AgentEvent.ToolFinished finished)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });

        var mark = finished.Ok ? "[green]ok[/]" : "[red]failed[/]";
        console.Write(new Markup($"[grey]tool[/] {Markup.Escape(finished.Tool)} {mark}: {Markup.Escape(finished.Summary)}\n"));
        return writer.ToString().TrimEnd('\r', '\n');
    }
}
