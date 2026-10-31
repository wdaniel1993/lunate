namespace Lunate.Tui;

internal sealed record SelectListQuestionOption(
    string Label,
    string Description,
    double? DecisionSuggestedProbability = null
);

internal enum QuestionPromptStatus
{
    Pending,
    Submitted,
    Skipped,
}

internal sealed record SelectListQuestionAnswer(
    IReadOnlyList<string> SelectedOptions,
    string? FreeText
);

/// <summary>
/// An immutable question picker state. Input produces a new value so the active option, selected
/// labels and free text remain part of the state used by every repaint.
/// </summary>
internal sealed record SelectListQuestionModel
{
    private SelectListQuestionModel(
        string title,
        string description,
        IReadOnlyList<SelectListQuestionOption> options,
        bool multiSelect,
        string? recommendedLabel
    )
    {
        Title = title;
        Description = description;
        Options = OrderRecommendedFirst(options, recommendedLabel);
        MultiSelect = multiSelect;
        RecommendedLabel = recommendedLabel;
    }

    public string Title { get; }

    public string Description { get; }

    public IReadOnlyList<SelectListQuestionOption> Options { get; }

    public bool MultiSelect { get; }

    public string? RecommendedLabel { get; }

    public int ActiveOption { get; private init; }

    public IReadOnlyList<int> SelectedIndices { get; private init; } = [];

    private InputLineState FreeTextInput { get; init; } = InputLine.Empty;

    public string? FreeText => FreeTextInput.Text.Length == 0 ? null : FreeTextInput.Text;

    public bool FreeTextFocused { get; private init; }

    public QuestionPromptStatus Status { get; private init; }

    public SelectListQuestionAnswer? Answer { get; private init; }

    public IReadOnlyList<string> SelectedLabels =>
        [.. SelectedIndices.Select(index => Options[index].Label)];

    public static SelectListQuestionModel Create(
        string title,
        string description,
        IReadOnlyList<SelectListQuestionOption> options,
        bool multiSelect,
        string? recommendedLabel = null
    )
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(options);
        return new SelectListQuestionModel(
            title,
            description,
            options,
            multiSelect,
            recommendedLabel
        );
    }

    public SelectListQuestionModel Handle(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (Status != QuestionPromptStatus.Pending || key.Ctrl || key.Alt)
        {
            return this;
        }

        if (key.Kind is KeyKind.Up or KeyKind.Down)
        {
            return key.Shift ? this : Move(key.Kind == KeyKind.Up ? -1 : 1);
        }

        if (key.Kind == KeyKind.Tab && !key.Shift)
        {
            return this with { FreeTextFocused = !FreeTextFocused };
        }

        if (key.Kind == KeyKind.Enter && !key.Shift)
        {
            return Submit();
        }

        if (key.Kind == KeyKind.Escape && !key.Shift)
        {
            return Skip();
        }

        if (FreeTextFocused)
        {
            return EditFreeText(key);
        }

        if (key is { Kind: KeyKind.Character, Shift: false, Text: { } text })
        {
            if (text.Length == 1 && char.IsAsciiDigit(text[0]))
            {
                int index = (int)char.GetNumericValue(text[0]) - 1;
                if (index < 0 || index >= Options.Count)
                {
                    return this;
                }

                return MultiSelect ? Toggle(index) : this with { ActiveOption = index };
            }
        }

        if (key is { Kind: KeyKind.Space, Shift: false } && MultiSelect)
        {
            return Toggle(ActiveOption);
        }

        return EditFreeText(key);
    }

    private SelectListQuestionModel Move(int delta) =>
        Options.Count == 0
            ? this
            : this with
            {
                ActiveOption = Math.Clamp(ActiveOption + delta, 0, Options.Count - 1),
            };

    private SelectListQuestionModel EditFreeText(KeyEvent key) =>
        key.Kind
            is KeyKind.Character
                or KeyKind.Space
                or KeyKind.Paste
                or KeyKind.Backspace
                or KeyKind.Delete
                or KeyKind.Left
                or KeyKind.Right
                or KeyKind.Home
                or KeyKind.End
            ? this with
            {
                FreeTextFocused = true,
                FreeTextInput = InputLine.Apply(FreeTextInput, key),
            }
            : this;

    private SelectListQuestionModel Toggle(int index)
    {
        if (index < 0 || index >= Options.Count)
        {
            return this;
        }

        int[] selected = SelectedIndices.Contains(index)
            ? [.. SelectedIndices.Where(selectedIndex => selectedIndex != index)]
            : [.. SelectedIndices, index];
        Array.Sort(selected);
        return this with { ActiveOption = index, SelectedIndices = selected };
    }

    private SelectListQuestionModel Submit()
    {
        IReadOnlyList<string> selected =
            MultiSelect ? SelectedLabels
            : Options.Count == 0 ? []
            : [Options[ActiveOption].Label];
        return this with
        {
            Status = QuestionPromptStatus.Submitted,
            Answer = new SelectListQuestionAnswer(selected, FreeText),
        };
    }

    private SelectListQuestionModel Skip() =>
        this with
        {
            Status = QuestionPromptStatus.Skipped,
            SelectedIndices = [],
            FreeTextInput = InputLine.Empty,
            FreeTextFocused = false,
            Answer = null,
        };

    private static IReadOnlyList<SelectListQuestionOption> OrderRecommendedFirst(
        IReadOnlyList<SelectListQuestionOption> options,
        string? recommendedLabel
    )
    {
        if (recommendedLabel is null)
        {
            return options.ToArray();
        }

        int index = -1;
        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Label, recommendedLabel, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index <= 0)
        {
            return options.ToArray();
        }

        var ordered = new List<SelectListQuestionOption>(options.Count) { options[index] };
        for (int i = 0; i < options.Count; i++)
        {
            if (i != index)
            {
                ordered.Add(options[i]);
            }
        }

        return ordered;
    }
}
