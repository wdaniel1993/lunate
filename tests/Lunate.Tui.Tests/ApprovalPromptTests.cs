namespace Lunate.Tui.Tests;

public sealed class ApprovalPromptTests
{
    private static readonly ApprovalPromptModel Prompt = new("bash", "dotnet format");

    public static TheoryData<string, ApprovalChoice> Decisions =>
        new()
        {
            { "y", ApprovalChoice.Approve },
            { "n", ApprovalChoice.Deny },
            { "a", ApprovalChoice.Always },
        };

    [Theory]
    [MemberData(nameof(Decisions))]
    public void Bare_decision_keys_decide(string text, ApprovalChoice expected) =>
        Assert.Equal(expected, Prompt.Decide(Key(KeyKind.Character, text)));

    [Fact]
    public void Bare_enter_denies_as_the_safe_default() =>
        Assert.Equal(ApprovalChoice.Deny, Prompt.Decide(Key(KeyKind.Enter)));

    [Fact]
    public void Escape_does_not_decide() => Assert.Null(Prompt.Decide(Key(KeyKind.Escape)));

    [Theory]
    [InlineData(KeyKind.Character, "y", true, false, false)]
    [InlineData(KeyKind.Character, "n", false, true, false)]
    [InlineData(KeyKind.Character, "a", false, false, true)]
    [InlineData(KeyKind.Enter, null, true, false, false)]
    [InlineData(KeyKind.Enter, null, false, true, false)]
    [InlineData(KeyKind.Enter, null, false, false, true)]
    public void Modifiers_never_decide(
        KeyKind kind,
        string? text,
        bool ctrl,
        bool shift,
        bool alt
    ) => Assert.Null(Prompt.Decide(new KeyEvent(kind, text, ctrl, shift, alt)));

    [Theory]
    [InlineData("Y")]
    [InlineData("N")]
    [InlineData("A")]
    [InlineData("x")]
    [InlineData("")]
    public void Non_decision_characters_do_not_decide(string text) =>
        Assert.Null(Prompt.Decide(Key(KeyKind.Character, text)));

    [Theory]
    [InlineData(KeyKind.Space)]
    [InlineData(KeyKind.Up)]
    [InlineData(KeyKind.Tab)]
    [InlineData(KeyKind.Paste)]
    public void Other_keys_do_not_decide(KeyKind kind) => Assert.Null(Prompt.Decide(Key(kind)));

    private static KeyEvent Key(
        KeyKind kind,
        string? text = null,
        bool ctrl = false,
        bool shift = false,
        bool alt = false
    ) => new(kind, text, ctrl, shift, alt);
}
