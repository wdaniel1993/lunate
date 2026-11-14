using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// The session's selected container environment: resolved during composition (before the header,
/// so the header can name it), passed to the tool composition, read by <c>/context</c> for its
/// readiness, and disposed on every session exit path. No selection creates nothing, and nothing
/// about the local path changes.
/// </summary>
internal sealed partial class InteractiveSession
{
    private ContainerExecutionEnvironment? _executionEnvironment;

    private void ResolveExecutionEnvironment()
    {
        _executionEnvironment = ContainerExecutionEnvironment.Create(
            _settings.Environment,
            _workspace.WorktreeRoot,
            _settings.BashGuard,
            _bashGuard
        );
    }

    /// <summary>The environment's current readiness, or null for the local default.</summary>
    private ExecutionEnvironmentStatus? EnvironmentStatus => _executionEnvironment?.Status;

    private async ValueTask DisposeExecutionEnvironmentAsync()
    {
        if (_executionEnvironment is not { } environment)
        {
            return;
        }

        _executionEnvironment = null;
        try
        {
            await environment.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (_options.Diagnostics ?? Console.Error).WriteLine(
                $"lunate: the container environment did not stop cleanly: {exception.Message}"
            );
        }
    }
}
