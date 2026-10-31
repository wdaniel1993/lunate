using Lunate.Tui;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private BashGuard? _bashGuard;
    private BashGuardStatus _bashGuardStatus = BashGuardStatus.Disabled;

    private void ResolveBashGuard()
    {
        if (!_settings.BashGuard)
        {
            _bashGuard = null;
            _bashGuardStatus = BashGuardStatus.Disabled;
            return;
        }

        _bashGuard = new BashGuard(
            GuardTarget.Resolve(),
            _options.BashGuardProbe ?? GuardProbe.Current
        );
        if (new ShellResolver().Resolve() is { } shell)
        {
            _bashGuard.Wrap(shell, _workspace.WorktreeRoot);
        }

        _bashGuardStatus = _bashGuard.Status;
    }
}
