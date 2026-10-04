namespace S5.Harness;

/// <summary>
/// Black-box seam every variant presents to the shared scenario and test list.
/// Time is virtual: <see cref="Advance"/> moves the variant's clock (a
/// <c>FakeTimeProvider</c> for A, a <c>TestScheduler</c> for B/B+).
/// </summary>
public interface ISession : IDisposable
{
    FakeTerminal Terminal { get; }

    bool IsRunning { get; }

    bool IsToolRunning { get; }

    bool IsApprovalPending { get; }

    bool IsCancelRequested { get; }

    bool IsQuitRequested { get; }

    string TailText { get; }

    void Post(AgentEvent value);

    void Key(ConsoleKeyInfo key);

    void Resize(int width, int height);

    void Advance(TimeSpan delta);

    ValueTask DrainAsync();

    IReadOnlyList<string> Snapshot();
}
