using System.Globalization;

namespace Lunate.Tui.Tests;

public sealed class SelectListQuestionTests
{
    private static readonly SelectListQuestionOption[] Options =
    [
        new("Plan B", "A fallback."),
        new("Plan A", "The preferred plan."),
        new("Plan C", "A different approach."),
    ];

    [Fact]
    public void Recommended_option_is_first_marked_without_submission()
    {
        SelectListQuestionModel question = Create(recommended: "Plan A");

        IReadOnlyList<string> lines = SelectListRenderer.QuestionLines(question);

        Assert.Contains("Plan A", lines[2]);
        Assert.Contains("recommended", lines[2], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Plan B", question.Options[1].Label);
        Assert.Equal(QuestionPromptStatus.Pending, question.Status);
        Assert.Null(question.Answer);
    }

    [Fact]
    public void Decision_suggestion_is_distinct_from_recommended_under_any_culture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
        try
        {
            SelectListQuestionModel question = SelectListQuestionModel.Create(
                "Choose a plan",
                "Which approach should we use?",
                [
                    new("Plan B", "A fallback."),
                    new("Plan A", "The preferred plan.", DecisionSuggestedProbability: 0.82d),
                    new("Plan C", "A different approach."),
                ],
                multiSelect: false,
                recommendedLabel: "Plan B"
            );

            IReadOnlyList<string> lines = SelectListRenderer.QuestionLines(question);

            Assert.Contains(
                lines,
                line =>
                    line.Contains(
                        "Plan A (suggested · decision model 0.82)",
                        StringComparison.Ordinal
                    )
            );
            Assert.Contains(
                lines,
                line => line.Contains("Plan B (recommended)", StringComparison.Ordinal)
            );
            Assert.DoesNotContain(
                lines,
                line =>
                    line.Contains("Plan B (suggested · decision model", StringComparison.Ordinal)
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Arrows_move_the_active_option()
    {
        SelectListQuestionModel question = Create()
            .Handle(Key(KeyKind.Down))
            .Handle(Key(KeyKind.Up))
            .Handle(Key(KeyKind.Down));

        Assert.Equal(1, question.ActiveOption);
    }

    [Fact]
    public void Enter_submits_the_active_option()
    {
        SelectListQuestionModel submitted = Create()
            .Handle(Key(KeyKind.Down))
            .Handle(Key(KeyKind.Enter));

        Assert.Equal(QuestionPromptStatus.Submitted, submitted.Status);
        Assert.Equal(["Plan A"], submitted.Answer!.SelectedOptions);
    }

    [Fact]
    public void A_number_selects_a_single_option()
    {
        SelectListQuestionModel submitted = Create()
            .Handle(Key(KeyKind.Character, "2"))
            .Handle(Key(KeyKind.Enter));

        Assert.Equal(["Plan A"], submitted.Answer!.SelectedOptions);
    }

    [Fact]
    public void A_number_toggles_an_option_in_multi_select_mode()
    {
        SelectListQuestionModel question = Create(multiSelect: true)
            .Handle(Key(KeyKind.Character, "2"))
            .Handle(Key(KeyKind.Character, "3"));

        Assert.Equal(["Plan A", "Plan C"], question.SelectedLabels);

        SelectListQuestionModel submitted = question.Handle(Key(KeyKind.Enter));

        Assert.Equal(["Plan A", "Plan C"], submitted.Answer!.SelectedOptions);
    }

    [Fact]
    public void Space_toggles_the_active_multi_select_option()
    {
        SelectListQuestionModel question = Create(multiSelect: true)
            .Handle(Key(KeyKind.Down))
            .Handle(Key(KeyKind.Space));

        Assert.Equal(["Plan A"], question.SelectedLabels);
    }

    [Fact]
    public void Free_text_is_submitted_alongside_selected_labels()
    {
        SelectListQuestionModel submitted = Create(multiSelect: true)
            .Handle(Key(KeyKind.Character, "2"))
            .Handle(Key(KeyKind.Paste, "Use the internal route"))
            .Handle(Key(KeyKind.Enter));

        Assert.Equal(["Plan A"], submitted.Answer!.SelectedOptions);
        Assert.Equal("Use the internal route", submitted.Answer.FreeText);
    }

    [Fact]
    public void Free_text_focus_accepts_spaces_without_losing_selected_options()
    {
        SelectListQuestionModel submitted = Create(multiSelect: true)
            .Handle(Key(KeyKind.Character, "2"))
            .Handle(Key(KeyKind.Tab))
            .Handle(Key(KeyKind.Character, "Use"))
            .Handle(Key(KeyKind.Space))
            .Handle(Key(KeyKind.Character, "the"))
            .Handle(Key(KeyKind.Enter));

        Assert.Equal(["Plan A"], submitted.Answer!.SelectedOptions);
        Assert.Equal("Use the", submitted.Answer.FreeText);
    }

    [Fact]
    public void Escape_skips_without_returning_partial_answers()
    {
        SelectListQuestionModel skipped = Create(multiSelect: true)
            .Handle(Key(KeyKind.Character, "2"))
            .Handle(Key(KeyKind.Paste, "partial"))
            .Handle(Key(KeyKind.Escape));

        Assert.Equal(QuestionPromptStatus.Skipped, skipped.Status);
        Assert.Null(skipped.Answer);
        Assert.Empty(skipped.SelectedLabels);
        Assert.Null(skipped.FreeText);
    }

    [Fact]
    public void Question_state_survives_repaint()
    {
        SelectListQuestionModel question = Create(multiSelect: true)
            .Handle(Key(KeyKind.Character, "2"))
            .Handle(Key(KeyKind.Paste, "keep this"));

        IReadOnlyList<string> firstPaint = SelectListRenderer.QuestionLines(question);
        IReadOnlyList<string> secondPaint = SelectListRenderer.QuestionLines(question);

        Assert.Equal(firstPaint, secondPaint);
        Assert.Contains(
            "Plan A",
            firstPaint.Single(line => line.Contains("[x]", StringComparison.Ordinal))
        );
        Assert.Contains("keep this", firstPaint[^1]);
        Assert.Equal(["Plan A"], question.SelectedLabels);
        Assert.Equal("keep this", question.FreeText);
    }

    private static SelectListQuestionModel Create(
        bool multiSelect = false,
        string? recommended = null
    ) =>
        SelectListQuestionModel.Create(
            "Choose a plan",
            "Which approach should we use?",
            Options,
            multiSelect,
            recommended
        );

    private static KeyEvent Key(KeyKind kind, string? text = null) =>
        new(kind, text, Ctrl: false, Shift: false, Alt: false);
}
