using ReactiveUI;

namespace S5.VariantBPlus;

/// <summary>Status footer as a ReactiveUI view model. No view bindings; the render function subscribes.</summary>
public sealed class StatusFooterViewModel : ReactiveObject
{
    private string _model = string.Empty;
    private long _inputTokens;
    private long _outputTokens;
    private double _contextPercent;

    public string Model
    {
        get => _model;
        set
        {
            if (EqualityComparer<string>.Default.Equals(_model, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _model, value);
            this.RaisePropertyChanged(nameof(Text));
        }
    }

    public long InputTokens
    {
        get => _inputTokens;
        set
        {
            if (EqualityComparer<long>.Default.Equals(_inputTokens, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _inputTokens, value);
            this.RaisePropertyChanged(nameof(Text));
        }
    }

    public long OutputTokens
    {
        get => _outputTokens;
        set
        {
            if (EqualityComparer<long>.Default.Equals(_outputTokens, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _outputTokens, value);
            this.RaisePropertyChanged(nameof(Text));
        }
    }

    public double ContextPercent
    {
        get => _contextPercent;
        set
        {
            if (EqualityComparer<double>.Default.Equals(_contextPercent, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _contextPercent, value);
            this.RaisePropertyChanged(nameof(Text));
        }
    }

    public string Text
    {
        get
        {
            var parts = new List<string>();
            if (Model.Length > 0)
            {
                parts.Add(Model);
            }

            if (InputTokens + OutputTokens > 0)
            {
                parts.Add($"{InputTokens + OutputTokens} tok");
            }

            parts.Add(FormattableString.Invariant($"{ContextPercent:0.#}% ctx"));
            return string.Join(" · ", parts);
        }
    }
}
