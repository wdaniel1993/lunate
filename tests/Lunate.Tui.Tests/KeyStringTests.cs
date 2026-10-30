namespace Lunate.Tui.Tests;

/// <summary>
/// The key-spec grammar a remap accepts: modifiers first (<c>Ctrl</c>, <c>Alt</c>, <c>Shift</c>),
/// then one named key or a single character — the same rows <c>keys.json</c> declares and the same
/// spelling the help overlay prints, so a spec round-trips through
/// <see cref="KeyPatternFormatter.Describe"/>.
/// </summary>
public sealed class KeyStringTests
{
    [Theory]
    [InlineData("Esc", KeyKind.Escape)]
    [InlineData("Enter", KeyKind.Enter)]
    [InlineData("Tab", KeyKind.Tab)]
    [InlineData("Backspace", KeyKind.Backspace)]
    [InlineData("Delete", KeyKind.Delete)]
    [InlineData("Up", KeyKind.Up)]
    [InlineData("Down", KeyKind.Down)]
    [InlineData("Left", KeyKind.Left)]
    [InlineData("Right", KeyKind.Right)]
    [InlineData("PageDown", KeyKind.PageDown)]
    [InlineData("PgDn", KeyKind.PageDown)]
    [InlineData("F5", KeyKind.F5)]
    public void Named_keys_parse_to_their_kind(string spec, KeyKind kind)
    {
        Assert.True(KeyString.TryParse(spec, out KeyPattern pattern, out _));
        Assert.Equal(kind, pattern.Kind);
        Assert.Null(pattern.Text);
        Assert.False(pattern.Ctrl);
    }

    [Fact]
    public void Modifier_prefixes_parse()
    {
        Assert.True(KeyString.TryParse("Ctrl+J", out KeyPattern pattern, out _));
        Assert.Equal(KeyKind.Character, pattern.Kind);
        Assert.Equal("j", pattern.Text);
        Assert.True(pattern.Ctrl);
        Assert.False(pattern.Alt);
        Assert.False(pattern.Shift);

        Assert.True(KeyString.TryParse("Alt+D", out pattern, out _));
        Assert.Equal("d", pattern.Text);
        Assert.False(pattern.Ctrl);
        Assert.True(pattern.Alt);

        Assert.True(KeyString.TryParse("Ctrl+Shift+Left", out pattern, out _));
        Assert.Equal(KeyKind.Left, pattern.Kind);
        Assert.Null(pattern.Text);
        Assert.True(pattern.Ctrl);
        Assert.True(pattern.Shift);
    }

    [Theory]
    [InlineData("?", "?")]
    [InlineData("x", "x")]
    public void Unmodified_single_characters_are_plain_text_keys(string spec, string text)
    {
        Assert.True(KeyString.TryParse(spec, out KeyPattern pattern, out _));
        Assert.Equal(KeyKind.Character, pattern.Kind);
        Assert.Equal(text, pattern.Text);
        Assert.False(pattern.Ctrl);
    }

    [Theory]
    [InlineData("Ctrl+J")]
    [InlineData("Alt+Enter")]
    [InlineData("Shift+Tab")]
    [InlineData("Esc")]
    [InlineData("?")]
    [InlineData("Ctrl+Shift+Left")]
    public void A_spec_round_trips_through_the_help_spelling(string spec)
    {
        Assert.True(KeyString.TryParse(spec, out KeyPattern pattern, out string? error));
        Assert.Null(error);
        string spelled = KeyPatternFormatter.Describe(pattern);
        Assert.True(KeyString.TryParse(spelled, out KeyPattern second, out error));
        Assert.Null(error);
        Assert.Equal(pattern, second);
        Assert.Equal(spec, spelled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Cntrl+J")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Enter+Enter")]
    [InlineData("F13")]
    [InlineData("SSyy")]
    public void A_bad_spec_is_refused_with_the_accepted_names(string spec)
    {
        Assert.False(KeyString.TryParse(spec, out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains("Ctrl", error, StringComparison.Ordinal);
        Assert.Contains("Enter", error, StringComparison.Ordinal);
    }
}
