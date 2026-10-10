using Spectre.Console;

namespace Lunate.Tui.Tests;

public sealed class TerminalCapabilitiesTests
{
    private static readonly Func<int> NeverProbed = () =>
        throw new InvalidOperationException("the output code page must not be probed");

    [Fact]
    public void No_color_disables_colour_even_when_colorterm_claims_true_colour()
    {
        var capabilities = Detect(environment: Env(("NO_COLOR", "1"), ("COLORTERM", "truecolor")));

        Assert.Equal(ColorSystemSupport.NoColors, capabilities.Color);
    }

    [Fact]
    public void An_empty_no_color_is_ignored()
    {
        var capabilities = Detect(environment: Env(("NO_COLOR", ""), ("COLORTERM", "truecolor")));

        Assert.Equal(ColorSystemSupport.TrueColor, capabilities.Color);
    }

    [Theory]
    [InlineData("truecolor")]
    [InlineData("TRUECOLOR")]
    [InlineData("24bit")]
    [InlineData("xterm-truecolor")]
    [InlineData("24bit-more")]
    public void Colorterm_upgrades_the_colour_depth(string colorTerm)
    {
        var capabilities = Detect(environment: Env(("COLORTERM", colorTerm)));

        Assert.Equal(ColorSystemSupport.TrueColor, capabilities.Color);
    }

    [Fact]
    public void Without_overrides_the_spectre_detection_applies()
    {
        var capabilities = Detect(environment: Env());

        Assert.Equal(ColorSystemSupport.Detect, capabilities.Color);
    }

    [Fact]
    public void Non_interactive_output_is_colourless_regardless_of_the_environment()
    {
        var capabilities = Detect(
            isInteractive: false,
            environment: Env(("COLORTERM", "truecolor"), ("TERM", "xterm-256color"))
        );

        Assert.Equal(ColorSystemSupport.NoColors, capabilities.Color);
    }

    [Fact]
    public void Term_dumb_disables_unicode()
    {
        var capabilities = Detect(environment: Env(("TERM", "dumb"), ("LANG", "en_US.UTF-8")));

        Assert.False(capabilities.Unicode);
    }

    [Fact]
    public void Term_dumb_is_matched_case_insensitively()
    {
        var capabilities = Detect(environment: Env(("TERM", "DUMB")));

        Assert.False(capabilities.Unicode);
    }

    [Fact]
    public void Lunate_ascii_forces_ascii()
    {
        var capabilities = Detect(environment: Env(("LUNATE_ASCII", "1"), ("LANG", "en_US.UTF-8")));

        Assert.False(capabilities.Unicode);
    }

    [Fact]
    public void An_empty_lunate_ascii_is_ignored()
    {
        var capabilities = Detect(environment: Env(("LUNATE_ASCII", "")));

        Assert.True(capabilities.Unicode);
    }

    [Fact]
    public void An_empty_lunate_unicode_is_ignored()
    {
        var capabilities = Detect(
            isWindows: true,
            environment: Env(("LUNATE_UNICODE", "")),
            outputCodePage: () => 437
        );

        Assert.False(capabilities.Unicode);
    }

    [Fact]
    public void Lunate_unicode_forces_unicode()
    {
        var capabilities = Detect(environment: Env(("LUNATE_UNICODE", "1"), ("TERM", "dumb")));

        Assert.True(capabilities.Unicode);
    }

    [Fact]
    public void Lunate_ascii_wins_when_both_overrides_are_set()
    {
        var capabilities = Detect(
            isWindows: true,
            environment: Env(("LUNATE_ASCII", "1"), ("LUNATE_UNICODE", "1"), ("WT_SESSION", "1"))
        );

        Assert.False(capabilities.Unicode);
    }

    [Fact]
    public void Windows_terminal_session_is_unicode_without_probing_the_code_page()
    {
        var capabilities = Detect(
            isWindows: true,
            environment: Env(("WT_SESSION", "session-id")),
            outputCodePage: NeverProbed
        );

        Assert.True(capabilities.Unicode);
    }

    [Fact]
    public void An_empty_wt_session_falls_through_to_the_code_page()
    {
        var capabilities = Detect(
            isWindows: true,
            environment: Env(("WT_SESSION", "")),
            outputCodePage: () => 437
        );

        Assert.False(capabilities.Unicode);
    }

    [Theory]
    [InlineData(65001, true)]
    [InlineData(437, false)]
    [InlineData(0, false)]
    public void The_windows_console_code_page_decides_unicode(int codePage, bool expected)
    {
        var capabilities = Detect(isWindows: true, outputCodePage: () => codePage);

        Assert.Equal(expected, capabilities.Unicode);
    }

    [Fact]
    public void Unix_never_probes_the_output_code_page()
    {
        var capabilities = Detect(
            isWindows: false,
            environment: Env(("LANG", "en_US.UTF-8")),
            outputCodePage: NeverProbed
        );

        Assert.True(capabilities.Unicode);
    }

    [Fact]
    public void An_unset_unix_locale_means_unicode()
    {
        var capabilities = Detect(environment: Env());

        Assert.True(capabilities.Unicode);
    }

    [Theory]
    [InlineData("C.UTF-8", true)]
    [InlineData("en_US.utf8", true)]
    [InlineData("en_US.UTF-8", true)]
    [InlineData("C", false)]
    [InlineData("de_AT", false)]
    public void The_unix_locale_decides_unicode(string locale, bool expected)
    {
        var capabilities = Detect(environment: Env(("LANG", locale)));

        Assert.Equal(expected, capabilities.Unicode);
    }

    [Fact]
    public void The_first_non_empty_unix_locale_wins()
    {
        var capabilities = Detect(environment: Env(("LC_CTYPE", "C"), ("LANG", "en_US.UTF-8")));

        Assert.False(capabilities.Unicode);
    }

    [Fact]
    public void Lc_all_wins_over_the_other_locale_variables()
    {
        var capabilities = Detect(
            environment: Env(("LC_ALL", "en_US.UTF-8"), ("LC_CTYPE", "C"), ("LANG", "C"))
        );

        Assert.True(capabilities.Unicode);
    }

    [Fact]
    public void An_empty_locale_variable_is_skipped()
    {
        var capabilities = Detect(environment: Env(("LC_ALL", ""), ("LANG", "en_US.UTF-8")));

        Assert.True(capabilities.Unicode);
    }

    private static TerminalCapabilities Detect(
        bool isInteractive = true,
        bool isWindows = false,
        Func<string, string?>? environment = null,
        Func<int>? outputCodePage = null
    ) =>
        TerminalCapabilities.Detect(
            isInteractive,
            isWindows,
            environment ?? Env(),
            outputCodePage ?? NeverProbed
        );

    private static Func<string, string?> Env(params (string Name, string? Value)[] variables)
    {
        var values = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return name => values.GetValueOrDefault(name);
    }
}
