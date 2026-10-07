using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Testing.Tests;

public sealed class ExtensionTestHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_and_stop_are_idempotent_and_run_the_real_lifecycle()
    {
        using var temp = new TempDirectory();
        await using var host = HelloHost(temp);

        await host.StartAsync(Ct);
        await host.StartAsync(Ct);

        Assert.Equal("hello", host.LoadedExtension.Id);
        Assert.Equal(1, host.Trust.Calls);
        Assert.Equal(1, host.Trust.Approvals);
        Assert.True(File.Exists(Path.Combine(host.StorePath, "trust.json")));
        Assert.Single(
            host.Log.Entries,
            entry => entry.Message.Contains("hello service started", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            host.Log.Entries,
            entry => entry.Message.Contains("hello service stopped", StringComparison.Ordinal)
        );

        await host.StopAsync(Ct);
        await host.StopAsync(Ct);

        Assert.Single(
            host.Log.Entries,
            entry => entry.Message.Contains("hello service stopped", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Every_state_path_stays_under_the_caller_temp_directory()
    {
        using var temp = new TempDirectory();
        await using var host = HelloHost(temp);

        Assert.StartsWith(temp.Root, host.StorePath, StringComparison.Ordinal);
        Assert.StartsWith(temp.Root, host.WorkingDirectory, StringComparison.Ordinal);
        Assert.StartsWith(temp.Root, host.ExtensionDirectory, StringComparison.Ordinal);
        Assert.StartsWith(temp.Root, host.SessionPath, StringComparison.Ordinal);
        Assert.StartsWith(host.WorkingDirectory, host.ExtensionDirectory, StringComparison.Ordinal);

        await host.RunAsync(new SingleReplyChatClient(), "hello", Ct);

        Assert.True(File.Exists(host.SessionPath));
        Assert.StartsWith(temp.Root, host.SessionPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_replays_the_prompt_and_exposes_events_and_the_session()
    {
        using var temp = new TempDirectory();
        await using var host = HelloHost(temp);

        ExtensionRunResult result = await host.RunAsync(
            new SingleReplyChatClient("hello back"),
            "hello there",
            Ct
        );

        Assert.True(
            host.Events.OccurredBefore<Lunate.Agent.RunStarted, Lunate.Agent.RunFinished>()
        );
        Assert.Single(result.Events.OfType<Lunate.Agent.RunStarted>());
        Assert.Equal(
            [Lunate.Agent.StopReasons.Stop],
            result.Events.OfType<Lunate.Agent.RunFinished>().Select(finished => finished.StopReason)
        );
        Assert.Equal(
            "hello back",
            string.Concat(
                host.Events.Of<Lunate.Agent.TextMessageContent>().Select(content => content.Text)
            )
        );
        Assert.Equal(2, SessionAssertions.CountMessages(host.SessionPath));
        Assert.Equal(1, SessionAssertions.CountMessages(host.SessionPath, ChatRole.Assistant));
    }

    [Fact]
    public async Task Dispose_without_start_or_stop_is_quiet_and_idempotent()
    {
        using var temp = new TempDirectory();
        var host = HelloHost(temp);

        await host.DisposeAsync();
        await host.DisposeAsync();

        Assert.Equal(0, host.Trust.Calls);
    }

    private static ExtensionTestHost HelloHost(TempDirectory temp) =>
        new(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "hello",
                ExtensionDirectory = Path.Combine(
                    AppContext.BaseDirectory,
                    "TestExtensions",
                    "HelloExtension"
                ),
                SourceFiles = new Dictionary<string, string>
                {
                    ["extension.json"] =
                        """{"id":"hello","version":"0.1.0","apiVersion":"^1.2.0","entryAssembly":"HelloExtension.dll"}""",
                },
            }
        );
}
