using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>The platform clipboard helper: temp directory, size cap and timeout reporting.</summary>
public sealed class ClipboardImageTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_oversize_helper_output_is_refused_before_reading_it()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        long length = ImageLimits.MaxBytes + 1;
        var runner = new FakeRunner(request =>
        {
            using var stream = new FileStream(
                request.Arguments[^1],
                FileMode.Create,
                FileAccess.Write
            );
            stream.SetLength(length);
            return Ok();
        });
        var source = new PlatformClipboardImageSource(runner);

        ClipboardImageResult result = await source.ReadAsync(Ct);

        Assert.Equal(ClipboardImageStatus.TooLarge, result.Status);
        Assert.Equal(length, result.Length);
        Assert.Empty(result.Data.ToArray());
        Assert.False(Directory.Exists(Assert.Single(runner.Requests).WorkingDirectory));
    }

    [Fact]
    public async Task A_timed_out_helper_is_reported_as_a_timeout()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runner = new FakeRunner(_ => new ProcessRunResult(
            null,
            string.Empty,
            string.Empty,
            TimedOut: true
        ));
        var source = new PlatformClipboardImageSource(runner);

        ClipboardImageResult result = await source.ReadAsync(Ct);

        Assert.Equal(ClipboardImageStatus.TimedOut, result.Status);
        Assert.Contains("paste", result.Helper, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_helper_writes_into_a_private_directory_that_is_removed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        UnixFileMode? mode = null;
        string? directory = null;
        var runner = new FakeRunner(request =>
        {
            string dir = request.WorkingDirectory;
            directory = dir;
            mode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(dir);
            File.WriteAllBytes(request.Arguments[^1], ImageSamples.Png(2, 3));
            return Ok();
        });
        var source = new PlatformClipboardImageSource(runner);

        ClipboardImageResult result = await source.ReadAsync(Ct);

        Assert.Equal(ClipboardImageStatus.Image, result.Status);
        Assert.Equal(ImageSamples.Png(2, 3), result.Data.ToArray());
        Assert.NotNull(directory);
        Assert.NotEqual(Path.GetTempPath(), directory);
        Assert.StartsWith(
            "lunate-clipboard-",
            Path.GetFileName(directory!),
            StringComparison.Ordinal
        );
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            mode
        );
        Assert.False(Directory.Exists(directory));
    }

    private static ProcessRunResult Ok() => new(0, string.Empty, string.Empty, TimedOut: false);

    private sealed class FakeRunner(Func<ProcessRunRequest, ProcessRunResult> run) : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(run(request));
        }

        public Task<IProcessSession> StartSessionAsync(
            ProcessSessionRequest request,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }
}
