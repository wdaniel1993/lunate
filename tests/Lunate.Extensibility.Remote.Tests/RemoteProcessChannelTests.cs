using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// The production channel adapter: output-line protocol errors surface as
/// <see cref="RemoteProtocolException"/>, closed stdin as <see cref="RemoteChannelClosedException"/>,
/// and stderr is pumped into the extension log with the byte cap.
/// </summary>
public sealed class RemoteProcessChannelTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stderr_is_logged_until_the_cap_then_reported_once()
    {
        var log = new RecordingLog();
        var session = new FakeProcessSession { ErrorItems = ["one", "two", "three", "four"] };
        await using RemoteProcessChannel channel = await RemoteProcessChannel.StartAsync(
            new FakeProcessRunner(session),
            Request(),
            "ext",
            log,
            maxLogBytes: 8,
            cancellationToken: Ct
        );

        await channel.DisposeAsync();

        Assert.Equal(["extension 'ext': one", "extension 'ext': two"], log.Lines);
        Assert.Single(log.Warnings);
        Assert.Contains("cap", log.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_oversized_stderr_line_is_reported_and_pumping_continues()
    {
        var log = new RecordingLog();
        var session = new FakeProcessSession
        {
            ErrorItems = [new InvalidDataException("a line over 16 MB arrived"), "after"],
        };
        await using RemoteProcessChannel channel = await RemoteProcessChannel.StartAsync(
            new FakeProcessRunner(session),
            Request(),
            "ext",
            log,
            cancellationToken: Ct
        );

        await channel.DisposeAsync();

        Assert.Contains(
            log.Warnings,
            warning => warning.Contains("oversized stderr line", StringComparison.Ordinal)
        );
        Assert.Equal(["extension 'ext': after"], log.Lines);
    }

    [Fact]
    public async Task An_oversized_output_line_is_a_protocol_error()
    {
        var session = new FakeProcessSession { OutputOverflow = true };
        await using RemoteProcessChannel channel = await RemoteProcessChannel.StartAsync(
            new FakeProcessRunner(session),
            Request(),
            "ext",
            new RecordingLog(),
            cancellationToken: Ct
        );

        await Assert.ThrowsAsync<RemoteProtocolException>(async () =>
            await channel.ReadLineAsync(Ct)
        );
    }

    [Fact]
    public async Task A_closed_stdin_is_a_channel_closed_error()
    {
        var session = new FakeProcessSession { WriteFails = true };
        await using RemoteProcessChannel channel = await RemoteProcessChannel.StartAsync(
            new FakeProcessRunner(session),
            Request(),
            "ext",
            new RecordingLog(),
            cancellationToken: Ct
        );

        await Assert.ThrowsAsync<RemoteChannelClosedException>(async () =>
            await channel.WriteLineAsync("""{"jsonrpc":"2.0"}""", Ct)
        );
    }

    private static ProcessSessionRequest Request() =>
        new(
            "node",
            ["-e", ""],
            Path.GetTempPath(),
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

    private sealed class FakeProcessRunner(IProcessSession session) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IProcessSession> StartSessionAsync(
            ProcessSessionRequest request,
            CancellationToken ct
        ) => Task.FromResult(session);
    }

    private sealed class FakeProcessSession : IProcessSession
    {
        private readonly List<object> _errors = [];

        public IReadOnlyList<object> ErrorItems
        {
            init
            {
                _errors.Clear();
                _errors.AddRange(value);
            }
        }

        public bool OutputOverflow { get; init; }

        public bool WriteFails { get; init; }

        public bool HasExited => false;

        public int? ExitCode => null;

        public ValueTask<string?> ReadOutputLineAsync(CancellationToken cancellationToken) =>
            OutputOverflow
                ? throw new InvalidDataException("a line over 16 MB arrived")
                : ValueTask.FromResult<string?>(null);

        public ValueTask<string?> ReadErrorLineAsync(CancellationToken cancellationToken)
        {
            lock (_errors)
            {
                if (_errors.Count == 0)
                {
                    return ValueTask.FromResult<string?>(null);
                }

                object item = _errors[0];
                _errors.RemoveAt(0);
                return item switch
                {
                    Exception exception => throw exception,
                    string line => ValueTask.FromResult<string?>(line),
                    _ => ValueTask.FromResult<string?>(null),
                };
            }
        }

        public ValueTask WriteInputLineAsync(string line, CancellationToken cancellationToken) =>
            WriteFails
                ? throw new InvalidOperationException("the child's stdin is closed")
                : ValueTask.CompletedTask;

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public ValueTask KillAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingLog : IExtensionLog
    {
        public List<string> Lines { get; } = [];

        public List<string> Warnings { get; } = [];

        public void Info(string message) => Lines.Add(message);

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message) => Warnings.Add(message);
    }
}
