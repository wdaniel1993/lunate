using ReactiveUI;
using ReactiveUI.Primitives;
using S5.Harness;

namespace S5.VariantBPlus;

/// <summary>
/// Approval prompt as a ReactiveUI view model: the yes/no/always options are
/// <see cref="ReactiveCommand{TParam,TResult}"/>s that report the decision back
/// to the session. No view bindings; keys execute the commands directly.
/// </summary>
public sealed class ApprovalViewModel : ReactiveObject
{
    private string? _prompt;

    public ApprovalViewModel(Action<ApprovalDecision> onDecision)
    {
        YesCommand = ReactiveCommand.Create(() => onDecision(ApprovalDecision.Yes));
        NoCommand = ReactiveCommand.Create(() => onDecision(ApprovalDecision.No));
        AlwaysCommand = ReactiveCommand.Create(() => onDecision(ApprovalDecision.Always));
    }

    public string? Prompt
    {
        get => _prompt;
        set => this.RaiseAndSetIfChanged(ref _prompt, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> YesCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> NoCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> AlwaysCommand { get; }

    public bool TryResolve(ConsoleKeyInfo key)
    {
        var command = char.ToLowerInvariant(key.KeyChar) switch
        {
            'y' => YesCommand,
            'n' => NoCommand,
            'a' => AlwaysCommand,
            _ => null,
        };

        if (command is null)
        {
            return false;
        }

        System.ObservableExtensions.Subscribe(command.Execute());
        return true;
    }

    public void Sync(string? pendingApprovalText) =>
        Prompt = pendingApprovalText is null
            ? null
            : $"approve {pendingApprovalText}? [y]es [n]o [a]lways";
}
