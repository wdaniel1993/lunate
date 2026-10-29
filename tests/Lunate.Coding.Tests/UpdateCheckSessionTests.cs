using System.Net;
using System.Text;
using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

/// <summary>
/// The update check at the session seam: the cached release is one linked header line, a release
/// found in the background is one line after the header, a slow check never blocks the header, and
/// the opt-outs make no request at all. Print and ACP never construct the check.
/// </summary>
public sealed class UpdateCheckSessionTests
{
    private const string Newer = "9.9.9";
    private const string ChangelogUrl =
        "https://github.com/wdaniel1993/lunate/blob/main/CHANGELOG.md";
    private const string Line = Newer + " available · changelog";

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_cached_release_adds_one_linked_line_to_the_header()
    {
        using var cache = new TempDirectory();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Release()));
        UpdateCheckSeams seams = Seams(cache, handler);
        UpdateCheckCache.Write(
            seams.CachePath!,
            new UpdateCheckCache(Now.AddHours(-1), Newer, ChangelogUrl)
        );
        using var host = new InteractiveSessionHost(
            environment: KeyedEnvironment(),
            updateCheck: seams
        );

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        string scrollback = await host.WaitForScrollbackAsync(Line);

        Assert.Equal(1, Occurrences(scrollback, Line));
        Assert.Empty(handler.Requests);
        await QuitAsync(host, run);
    }

    [Fact]
    public async Task A_release_found_in_the_background_adds_one_line_after_the_header()
    {
        using var cache = new TempDirectory();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Release()));
        using var host = new InteractiveSessionHost(
            environment: KeyedEnvironment(),
            updateCheck: Seams(cache, handler)
        );

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        string scrollback = await host.WaitForScrollbackAsync(Line);

        Assert.Equal(1, Occurrences(scrollback, Line));
        Assert.Single(handler.Requests);
        await QuitAsync(host, run);
    }

    [Fact]
    public async Task A_slow_check_never_blocks_the_header()
    {
        using var cache = new TempDirectory();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(
            async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return Release();
            }
        );
        using var host = new InteractiveSessionHost(
            environment: KeyedEnvironment(),
            updateCheck: Seams(cache, handler)
        );

        Task run = host.RunAsync();
        await started.Task.WaitAsync(WaitBudgets.InProcess, TestContext.Current.CancellationToken);
        string scrollback = await host.WaitForScrollbackAsync($"lunate  v{ProductVersion.Build}");

        Assert.Contains($"lunate  v{ProductVersion.Build}", scrollback, StringComparison.Ordinal);
        Assert.DoesNotContain(Line, scrollback, StringComparison.Ordinal);
        await QuitAsync(host, run);
    }

    [Fact]
    public async Task Offline_makes_zero_requests_and_prints_no_line()
    {
        using var cache = new TempDirectory();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Release()));
        using var host = new InteractiveSessionHost(
            environment: KeyedEnvironment(),
            launch: new LaunchOptions { Offline = true },
            updateCheck: Seams(cache, handler)
        );

        await AssertNoCheckAsync(host, handler);
    }

    [Fact]
    public async Task The_setting_disables_the_check()
    {
        using var cache = new TempDirectory();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Release()));
        using var host = new InteractiveSessionHost(
            environment: KeyedEnvironment(),
            updateCheck: Seams(cache, handler)
        );
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{ "schemaVersion": 1, "updateCheck": false }"""
        );

        await AssertNoCheckAsync(host, handler);
    }

    [Fact]
    public async Task The_environment_variable_disables_the_check()
    {
        using var cache = new TempDirectory();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Release()));
        using var host = new InteractiveSessionHost(
            environment: Environment(("LUNATE_UPDATE_CHECK", "0")),
            updateCheck: Seams(cache, handler)
        );

        await AssertNoCheckAsync(host, handler);
    }

    [Fact]
    public async Task Print_mode_via_the_cli_makes_no_request_and_writes_no_cache()
    {
        using var temp = new TempDirectory();
        using var cache = new TempDirectory();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Release()));
        UpdateCheckSeams seams = Seams(cache, handler);
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        );
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["-p", "go"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            printOptions: options,
            updateCheck: seams
        );

        Assert.Equal(0, exitCode);
        Assert.Empty(handler.Requests);
        Assert.False(File.Exists(seams.CachePath));
    }

    [Fact]
    public void Acp_mode_via_the_cli_makes_no_request_and_writes_no_cache()
    {
        using var temp = new TempDirectory();
        using var cache = new TempDirectory();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Release()));
        UpdateCheckSeams seams = Seams(cache, handler);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Cli.Run(
            ["--acp"],
            output,
            errors,
            cancelled.Token,
            extensions: TestExtensionSeams.Empty(temp),
            updateCheck: seams
        );

        Assert.Empty(handler.Requests);
        Assert.False(File.Exists(seams.CachePath));
    }

    [Fact]
    public async Task Help_appends_the_linked_update_line_when_a_release_is_known()
    {
        using var cache = new TempDirectory();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Release()));
        UpdateCheckSeams seams = Seams(cache, handler);
        UpdateCheckCache.Write(
            seams.CachePath!,
            new UpdateCheckCache(Now.AddHours(-1), Newer, ChangelogUrl)
        );
        using var host = new InteractiveSessionHost(
            environment: KeyedEnvironment(),
            updateCheck: seams
        );

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("/help");
        host.Console.SendEnter();
        string scrollback = await host.WaitForScrollbackAsync(
            text => Occurrences(text, Line) == 2,
            "the update line in the header and in /help"
        );

        Assert.Equal(2, Occurrences(scrollback, Line));
        await QuitAsync(host, run);
    }

    private static UpdateCheckSeams Seams(TempDirectory cache, HttpMessageHandler handler) =>
        new()
        {
            CachePath = cache.File("update-check.json"),
            Handler = handler,
            Clock = () => Now,
        };

    private static async Task AssertNoCheckAsync(
        InteractiveSessionHost host,
        RecordingHandler handler
    )
    {
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        string scrollback = await host.ScrollbackAfterAsync(() => run);

        Assert.Empty(handler.Requests);
        Assert.DoesNotContain(Line, scrollback, StringComparison.Ordinal);
    }

    private static async Task QuitAsync(InteractiveSessionHost host, Task run)
    {
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static HttpResponseMessage Release() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { tag_name = "v" + Newer, html_url = ChangelogUrl }),
                Encoding.UTF8,
                "application/json"
            ),
        };

    private static int Occurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private static Func<string, string?> KeyedEnvironment() =>
        Environment(("LUNATE_UNICODE", "1"), ("OPENAI_API_KEY", "test-key"));

    private static Func<string, string?> Environment(params (string Name, string Value)[] overrides)
    {
        Func<string, string?> baseline = PrintModeTestSupport.Environment();
        return name =>
        {
            foreach ((string overridden, string value) in overrides)
            {
                if (string.Equals(name, overridden, StringComparison.Ordinal))
                {
                    return value;
                }
            }

            return baseline(name);
        };
    }
}
