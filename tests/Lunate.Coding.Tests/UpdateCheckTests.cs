using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The daily update check's contract: one request per day across processes, a cache that survives
/// every failure, semver precedence and the opt-out matrix. Every case is offline.
/// </summary>
public sealed class UpdateCheckTests
{
    private const string EndpointUrl = "https://example.test/releases/latest";
    private const string ChangelogUrl = "https://example.test/CHANGELOG.md";

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_fresh_cache_returns_the_cached_release_without_a_request()
    {
        using var temp = new TempDirectory();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Release("v9.9.9")));
        UpdateCheckSeams seams = Seams(temp, handler, Now);
        UpdateCheckCache.Write(
            seams.CachePath!,
            new UpdateCheckCache(Now.AddHours(-1), "9.9.9", ChangelogUrl)
        );

        UpdateInfo? info = await UpdateCheck.CheckAsync(
            seams,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new UpdateInfo("9.9.9", ChangelogUrl), info);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_cache_older_than_a_day_makes_one_request_and_is_replaced()
    {
        using var temp = new TempDirectory();
        using var handler = new RecordingHandler(
            (_, _) => Task.FromResult(Release("v9.9.9", "https://example.test/releases/v9.9.9"))
        );
        UpdateCheckSeams seams = Seams(temp, handler, Now);
        UpdateCheckCache.Write(
            seams.CachePath!,
            new UpdateCheckCache(Now.AddDays(-2), "0.1.0", ChangelogUrl)
        );

        UpdateInfo? info = await UpdateCheck.CheckAsync(
            seams,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("9.9.9", info?.Version);
        Assert.Equal("https://example.test/releases/v9.9.9", info?.ChangelogUrl);
        Assert.Single(handler.Requests);
        UpdateCheckCache? cache = UpdateCheckCache.Read(seams.CachePath!);
        Assert.NotNull(cache);
        Assert.Equal(Now, cache.CheckedAt);
        Assert.Equal("9.9.9", cache.LatestVersion);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.Forbidden, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task A_failed_check_is_silent_and_consumes_the_day(
        HttpStatusCode status,
        string body
    )
    {
        using var temp = new TempDirectory();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Response(status, body)));
        UpdateCheckSeams seams = Seams(temp, handler, Now);

        UpdateInfo? first = await UpdateCheck.CheckAsync(
            seams,
            TestContext.Current.CancellationToken
        );
        UpdateInfo? second = await UpdateCheck.CheckAsync(
            seams,
            TestContext.Current.CancellationToken
        );

        Assert.Null(first);
        Assert.Null(second);
        Assert.Single(handler.Requests);
        UpdateCheckCache? cache = UpdateCheckCache.Read(seams.CachePath!);
        Assert.NotNull(cache);
        Assert.Null(cache.LatestVersion);
        Assert.Null(cache.ChangelogUrl);
    }

    [Fact]
    public async Task The_user_agent_is_the_product_name_and_version_only()
    {
        using var temp = new TempDirectory();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Release("v9.9.9")));
        UpdateCheckSeams seams = Seams(temp, handler, Now);

        await UpdateCheck.CheckAsync(seams, TestContext.Current.CancellationToken);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal($"lunate/{ProductVersion.Display}", request.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("0.1.0", "0.1.0-rc.2", true)]
    [InlineData("v0.2.0", "0.1.0", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("garbage", "0.1.0", false)]
    public void A_release_is_newer_only_when_its_semver_precedence_is_higher(
        string latest,
        string current,
        bool expected
    )
    {
        UpdateInfo? result = UpdateCheck.IsNewer(new UpdateInfo(latest, ChangelogUrl), current);

        Assert.Equal(expected, result is not null);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("one.two.three")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0+")]
    public void Malformed_versions_do_not_parse(string text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Fact]
    public void The_policy_is_enabled_by_default()
    {
        Assert.True(
            UpdateCheckPolicy.Enabled(
                new AgentSettings(),
                _ => null,
                new LaunchOptions(),
                UpdateCheckMode.Interactive
            )
        );
    }

    [Theory]
    [InlineData("print")]
    [InlineData("acp")]
    public void The_machine_modes_never_check(string name)
    {
        UpdateCheckMode mode = name == "print" ? UpdateCheckMode.Print : UpdateCheckMode.Acp;

        Assert.False(
            UpdateCheckPolicy.Enabled(new AgentSettings(), _ => null, new LaunchOptions(), mode),
            name
        );
    }

    [Fact]
    public void The_setting_disables_the_check()
    {
        Assert.False(
            UpdateCheckPolicy.Enabled(
                new AgentSettings { UpdateCheck = false },
                _ => null,
                new LaunchOptions(),
                UpdateCheckMode.Interactive
            )
        );
    }

    [Fact]
    public void The_environment_variable_disables_the_check()
    {
        using var temp = new TempDirectory();
        AgentSettings settings = SettingsStore.Resolve(
            temp.File("missing.json"),
            name => name == "LUNATE_UPDATE_CHECK" ? "0" : null
        );

        Assert.False(
            UpdateCheckPolicy.Enabled(
                settings,
                _ => null,
                new LaunchOptions(),
                UpdateCheckMode.Interactive
            )
        );
    }

    [Fact]
    public void Offline_disables_the_check()
    {
        Assert.False(
            UpdateCheckPolicy.Enabled(
                new AgentSettings(),
                _ => null,
                new LaunchOptions { Offline = true },
                UpdateCheckMode.Interactive
            )
        );
    }

    [Theory]
    [InlineData("true", false)]
    [InlineData("1", false)]
    [InlineData("yes", false)]
    [InlineData("0", true)]
    [InlineData("false", true)]
    [InlineData(" FALSE ", true)]
    public void Ci_disables_the_check_except_for_its_false_literals(string value, bool expected)
    {
        Assert.Equal(
            expected,
            UpdateCheckPolicy.Enabled(
                new AgentSettings(),
                name => name == "CI" ? value : null,
                new LaunchOptions(),
                UpdateCheckMode.Interactive
            )
        );
    }

    [Fact]
    public async Task The_cache_file_is_lf_utf8_without_bom_even_on_failure()
    {
        using var temp = new TempDirectory();
        using var handler = new RecordingHandler(
            (_, _) => Task.FromResult(Response(HttpStatusCode.InternalServerError, "{}"))
        );
        UpdateCheckSeams seams = Seams(temp, handler, Now);

        await UpdateCheck.CheckAsync(seams, TestContext.Current.CancellationToken);

        byte[] bytes = await File.ReadAllBytesAsync(
            seams.CachePath!,
            TestContext.Current.CancellationToken
        );
        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "the cache must not carry a BOM"
        );
        string text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.Contains("\"checkedAt\"", text, StringComparison.Ordinal);
        Assert.Contains("\"latestVersion\":null", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_checks_on_one_path_make_exactly_one_request()
    {
        using var temp = new TempDirectory();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(
            async (_, ct) =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(ct);
                return Release("v9.9.9");
            }
        );
        UpdateCheckSeams seams = Seams(temp, handler, Now);
        CancellationToken ct = TestContext.Current.CancellationToken;

        Task<UpdateInfo?> first = UpdateCheck.CheckAsync(seams, ct);
        await started.Task.WaitAsync(WaitBudgets.InProcess, ct);

        Task<UpdateInfo?> second = UpdateCheck.CheckAsync(seams, ct);
        Assert.Null(await second.WaitAsync(WaitBudgets.InProcess, ct));

        release.SetResult();
        UpdateInfo? info = await first.WaitAsync(WaitBudgets.InProcess, ct);
        Assert.Equal("9.9.9", info?.Version);
        Assert.Single(handler.Requests);
    }

    private static UpdateCheckSeams Seams(
        TempDirectory temp,
        HttpMessageHandler handler,
        DateTimeOffset now
    ) =>
        new()
        {
            CachePath = temp.File("update-check.json"),
            Handler = handler,
            Clock = () => now,
            Endpoint = EndpointUrl,
        };

    private static HttpResponseMessage Release(string? tag, string? url = null) =>
        Response(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new { tag_name = tag, html_url = url ?? ChangelogUrl })
        );

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
