using Lunate.Extensibility.Abstractions;

namespace FakeLsp;

/// <summary>
/// The <see cref="IBackgroundService"/> wrapper around <see cref="FakeLspServer"/>; the failure
/// flags let tests drive the start-failure and stop-failure paths.
/// </summary>
public sealed class FakeLspService(
    FakeLspServer server,
    bool failOnStart = false,
    bool failOnStop = false
) : IBackgroundService
{
    public FakeLspServer Server => server;

    public ValueTask StartAsync(CancellationToken cancellationToken) =>
        failOnStart
            ? ValueTask.FromException(new InvalidOperationException("lsp start exploded"))
            : server.StartAsync(cancellationToken);

    public ValueTask StopAsync(CancellationToken cancellationToken) =>
        failOnStop
            ? ValueTask.FromException(new InvalidOperationException("lsp stop exploded"))
            : server.StopAsync(cancellationToken);
}
