namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// An optional handler capability: reports one-time preparation time (for example a remote
/// extension's lazy child start and <c>initialize</c> handshake) that <see cref="IHookHandler"/>
/// implementors want excluded from the dispatch's measured elapsed time, so preparation never
/// feeds the hook's cost, its maximum or the slow-hook warning.
/// </summary>
public interface IPreparationTimeReporter
{
    /// <summary>
    /// Consumes and returns the preparation time to exclude from the current dispatch; zero when
    /// there is nothing to report. The hook runner calls this once per dispatch, after the
    /// handler returned.
    /// </summary>
    TimeSpan ConsumePreparationTime();
}
