namespace Lunate.Tui.Tests;

public sealed class SelectListQuestionWidthTests
{
    private const string SelectedLabel = "Recommended cross-platform migration plan";
    private const string SelectedDescription =
        "Preserve every existing setting, validate the destination before switching traffic, and keep a tested rollback ready.";

    [Theory]
    [InlineData(120, 4)]
    [InlineData(80, 4)]
    [InlineData(60, 4)]
    [InlineData(40, 2)]
    public void Question_layout_keeps_controls_and_selected_label_visible(
        int width,
        int descriptionIndent
    )
    {
        SelectListQuestionModel question = Question()
            .Handle(new KeyEvent(KeyKind.Character, "1", Ctrl: false, Shift: false, Alt: false));

        IReadOnlyList<string> lines = SelectListRenderer.QuestionLines(question, width);

        Assert.All(lines, line => Assert.True(CellText.Width(line) <= width, line));
        Assert.Contains(lines, line => line.Contains("[x]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("[ ]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Free text (Tab):", StringComparison.Ordinal));
        Assert.Contains("recommended", string.Join(' ', lines), StringComparison.OrdinalIgnoreCase);
        string firstDescription = lines.Single(line =>
            line.Contains("Preserve every", StringComparison.Ordinal)
        );
        Assert.Equal(
            descriptionIndent,
            firstDescription.Length - firstDescription.TrimStart().Length
        );
        AssertContainsWrapped(SelectedLabel, lines);
        AssertContainsWrapped(SelectedDescription, lines);
    }

    private static SelectListQuestionModel Question() =>
        SelectListQuestionModel.Create(
            "Choose a deployment strategy",
            "Which approach should the team use for the next production migration?",
            [
                new(SelectedLabel, SelectedDescription),
                new("Keep the current plan", "Make no change to the current deployment."),
            ],
            multiSelect: true,
            recommendedLabel: SelectedLabel
        );

    private static void AssertContainsWrapped(string text, IReadOnlyList<string> lines)
    {
        string expected = NormalizeWhitespace(text);
        string rendered = NormalizeWhitespace(string.Join(' ', lines));
        Assert.Contains(expected, rendered, StringComparison.Ordinal);
    }

    private static string NormalizeWhitespace(string text) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
