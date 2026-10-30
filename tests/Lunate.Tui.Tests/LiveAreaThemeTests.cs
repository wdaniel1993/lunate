using Microsoft.Reactive.Testing;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class LiveAreaThemeTests
{
    [Fact]
    public void Applying_a_theme_keeps_the_injected_scrollback_console()
    {
        var scheduler = new TestScheduler();
        using var terminal = new FakeConsoleIO(scheduler, new ConsoleSize(40, 10));
        var scrollback = new TestConsole();
        using var area = new LiveArea(
            terminal,
            scheduler,
            scrollback: scrollback,
            capabilities: new TerminalCapabilities(ColorSystemSupport.TrueColor, Unicode: true)
        );

        area.SetColor(ColorSystemSupport.Standard);
        area.WriteScrollback(console => console.WriteLine("settings persisted"));

        Assert.Contains("settings persisted", scrollback.Output, StringComparison.Ordinal);
    }
}
