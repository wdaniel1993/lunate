using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace MemoryProviderExtension.Tests;

public sealed class MemoryProviderExtensionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_remember_line_is_captured_and_committed_at_turn_end()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);

        await host.RunAsync(
            new ReplayChatClient(Fixture("memory-provider-capture-inject.jsonl")),
            "remember: alpha",
            Ct
        );
        await host.StopAsync(Ct);

        Assert.Contains(
            host.Log.Messages,
            message => message.Contains("remembered: alpha", StringComparison.Ordinal)
        );
        Assert.Contains(
            host.Log.Messages,
            message => message.Contains("committed 1 memory", StringComparison.Ordinal)
        );
        SessionExtensionEntry entry = Assert.Single(
            Session.Load(host.SessionPath).Entries.OfType<SessionExtensionEntry>()
        );
        Assert.Equal("ext/memory-provider/committed", entry.ExtensionType);
    }

    [Fact]
    public async Task The_next_request_contains_the_injected_memory_section()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);
        var client = new ReplayChatClient(Fixture("memory-provider-capture-inject.jsonl"));

        await host.RunAsync(client, "remember: alpha", Ct);
        await host.RunAsync(client, "go", Ct);
        await host.StopAsync(Ct);

        Assert.Contains(
            host.Log.Messages,
            message =>
                message.Contains("prepared memory section (1 memory;", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Re_scans_of_the_same_marker_do_not_duplicate_the_memory()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);
        var client = new ReplayChatClient(Fixture("memory-provider-capture-inject.jsonl"));

        await host.RunAsync(client, "remember: alpha", Ct);
        await host.RunAsync(client, "go", Ct);
        await host.RunAsync(new TextReplyChatClient("done"), "again", Ct);
        await host.StopAsync(Ct);

        Assert.Single(
            host.Log.Messages,
            message => message.Contains("remembered: alpha", StringComparison.Ordinal)
        );
        SessionExtensionEntry[] entries =
        [
            .. Session.Load(host.SessionPath).Entries.OfType<SessionExtensionEntry>(),
        ];
        Assert.Equal([1, 0, 0], entries.Select(entry => (int)entry.Payload!["count"]!));
        Assert.All(
            host.Log.Messages.Where(message =>
                message.Contains("prepared memory section", StringComparison.Ordinal)
            ),
            message => Assert.Contains("(1 memory;", message, StringComparison.Ordinal)
        );
        Assert.Equal(
            2,
            host.Log.Messages.Count(message =>
                message.Contains("prepared memory section", StringComparison.Ordinal)
            )
        );
    }

    [Fact]
    public async Task A_memory_survives_compaction_by_re_injection()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);
        SeedLongSession(host);
        var client = new ReplayChatClient(Fixture("memory-provider-compaction-reinjection.jsonl"));

        ExtensionRunResult first = await host.RunAsync(client, "remember: alpha", Ct);
        await host.RunAsync(client, "go", Ct);
        await host.StopAsync(Ct);

        Assert.Single(first.Events.OfType<CompactionApplied>());
        Assert.Contains(
            host.Log.Messages,
            message =>
                message.Contains("prepared memory section (1 memory;", StringComparison.Ordinal)
        );
        Assert.Equal(2, SessionAssertions.CountExtensions(host.SessionPath, "memory-provider"));
    }

    [Fact]
    public async Task The_store_starts_and_stops_once()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);

        await host.RunAsync(
            new ReplayChatClient(Fixture("memory-provider-capture-inject.jsonl")),
            "remember: alpha",
            Ct
        );
        await host.StopAsync(Ct);

        Assert.Single(
            host.Log.Entries,
            entry => entry.Message.Contains("service started", StringComparison.Ordinal)
        );
        Assert.Single(
            host.Log.Entries,
            entry => entry.Message.Contains("service stopped", StringComparison.Ordinal)
        );

        await host.DisposeAsync();

        Assert.Single(
            host.Log.Entries,
            entry => entry.Message.Contains("service started", StringComparison.Ordinal)
        );
        Assert.Single(
            host.Log.Entries,
            entry => entry.Message.Contains("service stopped", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task An_over_budget_addition_is_dropped_and_logged_with_the_extension_id()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(
            temp,
            new HookRunnerOptions { ContextBudgetPerExtension = 2 }
        );
        var client = new TextReplyChatClient("noted", "done");

        await host.RunAsync(client, "remember: alpha", Ct);
        await host.RunAsync(client, "go", Ct);
        await host.StopAsync(Ct);

        Assert.Contains(
            host.Log.Messages,
            message =>
                message.Contains("memory-provider", StringComparison.Ordinal)
                && message.Contains("was dropped", StringComparison.Ordinal)
                && message.Contains("budget of 2", StringComparison.Ordinal)
        );
    }

    internal static void SeedLongSession(ExtensionTestHost host)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(host.SessionPath)!);
        Session session = Session.Create(
            host.SessionPath,
            host.WorkingDirectory,
            "test-repository",
            host.WorkingDirectory
        );
        session.AppendMessage(new ChatMessage(ChatRole.User, "one " + new string('x', 420_000)));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "noted one"));
        AppendTurn(session, "two");
        AppendTurn(session, "three");
        AppendTurn(session, "four");
    }

    private static void AppendTurn(Session session, string text)
    {
        session.AppendMessage(new ChatMessage(ChatRole.User, text));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "noted " + text));
    }

    private static ExtensionTestHost CreateHost(
        TempDirectory temp,
        HookRunnerOptions? hookRunnerOptions = null
    ) =>
        new(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "memory-provider",
                ExtensionDirectory = Path.Combine(
                    AppContext.BaseDirectory,
                    "MemoryProviderExtension"
                ),
                HookRunnerOptions = hookRunnerOptions,
            }
        );

    private static string Fixture(string name) =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "fixtures", "streams", name);
}
