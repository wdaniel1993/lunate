namespace Lunate.Tui.Tests;

public sealed class ConsoleSupportTests
{
    [Fact]
    public void Interactive_console_needs_no_diagnostic() =>
        Assert.Null(ConsoleSupport.Check(new FakeConsoleIO { IsInteractive = true }));

    [Fact]
    public void Windows_without_a_console_gets_the_windows_diagnostic()
    {
        var message = ConsoleSupport.Describe(
            new FakeConsoleIO { IsInteractive = false },
            isWindows: true
        );

        Assert.NotNull(message);
        Assert.Contains("Windows Terminal", message, StringComparison.Ordinal);
        Assert.Contains("pseudo-console", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unix_terminal_failure_gets_the_print_mode_diagnostic()
    {
        var message = ConsoleSupport.Describe(
            new FakeConsoleIO { IsInteractive = false },
            isWindows: false
        );

        Assert.NotNull(message);
        Assert.Contains("lunate -p", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, "xterm-256color", false, false, true)]
    [InlineData(false, false, null, false, false, true)]
    [InlineData(false, false, "dumb", false, false, false)]
    [InlineData(false, false, "DUMB", false, false, false)]
    [InlineData(false, false, "xterm-256color", true, true, true)]
    [InlineData(false, false, "xterm-256color", true, false, false)]
    [InlineData(false, true, "xterm-256color", false, false, false)]
    [InlineData(false, true, "xterm-256color", true, true, false)]
    [InlineData(true, false, "xterm-256color", false, false, false)]
    [InlineData(true, true, "dumb", true, false, false)]
    public void Interactive_detection_covers_every_environment(
        bool inputRedirected,
        bool outputRedirected,
        string? term,
        bool isWindows,
        bool windowsConsoleAttached,
        bool expected
    )
    {
        bool actual = SystemConsoleIO.ComputeIsInteractive(
            inputRedirected,
            outputRedirected,
            term,
            isWindows,
            windowsConsoleAttached
        );

        Assert.Equal(expected, actual);
    }
}
