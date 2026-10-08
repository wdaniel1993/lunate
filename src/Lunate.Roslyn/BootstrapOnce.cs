namespace Lunate.Roslyn;

/// <summary>
/// Runs <paramref name="register"/> exactly once and caches its result (thread-safe). The MSBuild
/// locator may only be registered once per process, so the production path is a single static
/// instance of this.
/// </summary>
internal sealed class BootstrapOnce(Func<BootstrapResult> register)
{
    private readonly object _gate = new();
    private BootstrapResult? _cached;

    public BootstrapResult EnsureInitialized()
    {
        lock (_gate)
        {
            return _cached ??= register();
        }
    }
}
