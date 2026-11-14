using Microsoft.Reactive.Testing;

namespace Lunate.Tui.Tests;

public sealed class SelectListQuestionArmingTests
{
    private static readonly SelectListQuestionOption[] Options =
    [
        new("Plan A", "The preferred plan."),
        new("Plan B", "A fallback."),
    ];

    [Fact]
    public void A_buffered_selection_key_is_ignored()
    {
        var scheduler = new TestScheduler();
        var prompt = new SelectListQuestionInteraction(scheduler);
        prompt.BeginQuestion(Create(), lastKeySequence: 5);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(301).Ticks);

        prompt.Handle(Key(KeyKind.Character, "2"), sequence: 5);

        Assert.Equal(0, prompt.Question.ActiveOption);
    }

    [Fact]
    public void A_buffered_submit_key_is_ignored()
    {
        var scheduler = new TestScheduler();
        var prompt = new SelectListQuestionInteraction(scheduler);
        prompt.BeginQuestion(Create(), lastKeySequence: 5);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(301).Ticks);

        prompt.Handle(Key(KeyKind.Enter), sequence: 5);

        Assert.Equal(QuestionPromptStatus.Pending, prompt.Question.Status);
        Assert.Null(prompt.Question.Answer);
    }

    [Fact]
    public void A_buffered_text_key_is_ignored()
    {
        var scheduler = new TestScheduler();
        var prompt = new SelectListQuestionInteraction(scheduler);
        prompt.BeginQuestion(Create(), lastKeySequence: 5);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(301).Ticks);

        prompt.Handle(Key(KeyKind.Paste, "too early"), sequence: 5);

        Assert.Null(prompt.Question.FreeText);
    }

    [Fact]
    public void Selection_keys_in_the_first_300_ms_are_ignored()
    {
        var scheduler = new TestScheduler();
        var prompt = new SelectListQuestionInteraction(scheduler);
        prompt.BeginQuestion(Create(), lastKeySequence: 0);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);

        prompt.Handle(Key(KeyKind.Down), sequence: 1);

        Assert.Equal(0, prompt.Question.ActiveOption);
    }

    [Fact]
    public void Text_input_in_the_first_300_ms_is_ignored()
    {
        var scheduler = new TestScheduler();
        var prompt = new SelectListQuestionInteraction(scheduler);
        prompt.BeginQuestion(Create(), lastKeySequence: 0);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(299).Ticks);

        prompt.Handle(Key(KeyKind.Paste, "too early"), sequence: 1);

        Assert.Null(prompt.Question.FreeText);
    }

    [Fact]
    public void Enter_in_the_first_300_ms_does_not_submit()
    {
        var scheduler = new TestScheduler();
        var prompt = new SelectListQuestionInteraction(scheduler);
        prompt.BeginQuestion(Create(), lastKeySequence: 0);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks);

        prompt.Handle(Key(KeyKind.Enter), sequence: 1);

        Assert.Equal(QuestionPromptStatus.Pending, prompt.Question.Status);
        Assert.Null(prompt.Question.Answer);
    }

    [Fact]
    public void Every_new_question_gets_its_own_300_ms_arm_window()
    {
        var scheduler = new TestScheduler();
        var prompt = new SelectListQuestionInteraction(scheduler);
        prompt.BeginQuestion(Create(), lastKeySequence: 0);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(301).Ticks);
        prompt.Handle(Key(KeyKind.Enter), sequence: 1);
        Assert.Equal(QuestionPromptStatus.Submitted, prompt.Question.Status);

        prompt.BeginQuestion(Create(), lastKeySequence: 1);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);
        prompt.Handle(Key(KeyKind.Down), sequence: 2);

        Assert.Equal(0, prompt.Question.ActiveOption);
        Assert.Equal(QuestionPromptStatus.Pending, prompt.Question.Status);
    }

    [Fact]
    public void Escape_skips_without_returning_partial_answers_after_arming()
    {
        var scheduler = new TestScheduler();
        var prompt = new SelectListQuestionInteraction(scheduler);
        prompt.BeginQuestion(Create(multiSelect: true), lastKeySequence: 0);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(301).Ticks);
        prompt.Handle(Key(KeyKind.Character, "2"), sequence: 1);
        prompt.Handle(Key(KeyKind.Paste, "partial"), sequence: 2);

        prompt.Handle(Key(KeyKind.Escape), sequence: 3);

        Assert.Equal(QuestionPromptStatus.Skipped, prompt.Question.Status);
        Assert.Null(prompt.Question.Answer);
        Assert.Empty(prompt.Question.SelectedLabels);
        Assert.Null(prompt.Question.FreeText);
    }

    /// <summary>
    /// The seam the session harness waits on: a question whose arm sits exactly on the window's
    /// edge reports the next key as dropped, and aging the arm — without moving the clock — makes
    /// that same key decide. A harness that waited for the picker's frame and then advanced a fixed
    /// 300 ms could land on the edge, and then the answer it typed was discarded and the run waited
    /// for a decision no key could deliver (#863).
    /// </summary>
    [Fact]
    public void The_arm_seam_ages_the_window_without_moving_the_clock()
    {
        var scheduler = new TestScheduler();
        var prompt = new SelectListQuestionInteraction(scheduler);
        prompt.BeginQuestion(Create(), lastKeySequence: 0);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);

        Assert.False(prompt.ArmedForNextKey(sequence: 1));
        DateTimeOffset now = scheduler.Now;

        prompt.ExpireArmWindow();

        Assert.Equal(now, scheduler.Now);
        Assert.True(prompt.ArmedForNextKey(sequence: 1));
        prompt.Handle(Key(KeyKind.Character, "2"), sequence: 1);
        prompt.Handle(Key(KeyKind.Enter), sequence: 2);
        Assert.Equal(QuestionPromptStatus.Submitted, prompt.Question.Status);
        Assert.Equal(["Plan B"], prompt.Question.Answer!.SelectedOptions);
    }

    /// <summary>
    /// Aging the window never defeats the sequence half of the guard: a key read before the question
    /// armed is still dropped, so the harness cannot press a key the pump would have discarded.
    /// </summary>
    [Fact]
    public void Aging_the_window_keeps_a_buffered_key_ignored()
    {
        var scheduler = new TestScheduler();
        var prompt = new SelectListQuestionInteraction(scheduler);
        prompt.BeginQuestion(Create(), lastKeySequence: 5);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);

        prompt.ExpireArmWindow();

        Assert.False(prompt.ArmedForNextKey(sequence: 5));
        prompt.Handle(Key(KeyKind.Enter), sequence: 5);
        Assert.Equal(QuestionPromptStatus.Pending, prompt.Question.Status);
    }

    private static SelectListQuestionModel Create(bool multiSelect = false) =>
        SelectListQuestionModel.Create("Choose a plan", "Select a plan.", Options, multiSelect);

    private static KeyEvent Key(KeyKind kind, string? text = null) =>
        new(kind, text, Ctrl: false, Shift: false, Alt: false);
}
