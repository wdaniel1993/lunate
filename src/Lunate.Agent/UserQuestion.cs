namespace Lunate.Agent;

internal sealed record UserQuestion(
    string Header,
    string Question,
    IReadOnlyList<UserQuestionOption> Options,
    bool MultiSelect,
    bool AllowFreeText
);

internal sealed record UserQuestionOption(
    string Label,
    string Description,
    bool Recommended,
    double? DecisionSuggestedProbability = null
);

internal sealed record UserQuestionAnswer(IReadOnlyList<string> SelectedOptions, string? Text);

internal sealed record UserQuestionResult(
    UserQuestionStatus Status,
    IReadOnlyList<UserQuestionAnswer> Answers
);

internal enum UserQuestionStatus
{
    Answered,
    Skipped,
    Unavailable,
}
