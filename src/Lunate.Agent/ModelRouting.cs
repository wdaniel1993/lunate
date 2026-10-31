using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// Resolves a model id to a chat client. The live implementation lives in the extensibility host:
/// it resolves virtual models, programmatic and declared extension providers, then the session
/// catalog, and throws a teaching error when nothing serves the id.
/// </summary>
public interface IModelRegistry
{
    /// <summary>
    /// Resolves the model id to a client. The same id returns a usable client on every call; a
    /// virtual model's client routes per request. Unknown ids throw a teaching error listing the
    /// known ids.
    /// </summary>
    IChatClient Resolve(string modelId);
}

/// <summary>
/// The route-observer ambient: a run-bound call wrapper scopes it around each model call and a
/// router client reports every resolution into it, so routed calls can be attributed (events,
/// usage, display) without the router knowing about runs.
/// </summary>
public static class ModelRouteObserver
{
    private static readonly AsyncLocal<Action<string, string>?> Active = new();

    /// <summary>
    /// Scopes <paramref name="onRouted"/> as the active observer until the returned scope is
    /// disposed; nested scopes restore the previous observer.
    /// </summary>
    public static IDisposable Scope(Action<string, string> onRouted)
    {
        ArgumentNullException.ThrowIfNull(onRouted);
        Action<string, string>? previous = Active.Value;
        Active.Value = onRouted;
        return new ScopeDisposable(previous);
    }

    /// <summary>
    /// Reports one resolution (requested → resolved) to the innermost active observer; a no-op
    /// when no observer is scoped.
    /// </summary>
    public static void Report(string requestedModelId, string resolvedModelId) =>
        Active.Value?.Invoke(requestedModelId, resolvedModelId);

    private sealed class ScopeDisposable(Action<string, string>? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Active.Value = previous;
        }
    }
}
