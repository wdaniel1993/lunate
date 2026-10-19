using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;
using Microsoft.Extensions.AI;
using TextStyle = Lunate.Extensibility.Abstractions.TextStyle;

namespace Lunate.Coding.Tests;

/// <summary>
/// The interactive mode's cells of the degradation matrix: notices reach the live area, status
/// widgets append to the footer, renderer contributions become styled blocks above the live area,
/// and a throwing contribution is omitted, reported and retried on the next update.
/// </summary>
public sealed class TuiInteractionTests
{
    [Fact]
    public async Task A_notice_is_presented_in_the_live_area()
    {
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                RetryBaseDelay = TimeSpan.Zero,
            }
        );
        host.Client.EnqueueFailure(new HttpRequestException("boom"));
        host.Client.Enqueue(Scripts.Text("recovered"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "retrying (attempt 1)");

        Assert.Contains("retrying (attempt 1)", host.Frames, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Status_widget_segments_append_to_the_footer()
    {
        var widgets = new StatusWidgetRegistry();
        widgets.Register("ext.widget", new FixedWidget([Segment("widget-on", TextStyle.Accent)]));
        using var host = new InteractiveSessionHost(statusWidgets: widgets);
        host.Console.Size = new ConsoleSize(200, 24);
        host.Client.Enqueue(UsageUpdate(), Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Frames.Contains("15/128.0k", StringComparison.Ordinal);
        });

        Assert.Contains("15/128.0k", host.Frames, StringComparison.Ordinal);
        Assert.Contains("widget-on", host.Frames, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_tool_result_renderer_renders_a_block_above_the_live_area()
    {
        string scrollback = await RunBlockScenarioAsync(environment: null);

        Assert.Contains("extension saw the read", scrollback, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_block_text_survives_the_no_color_and_ascii_fallbacks()
    {
        string defaultScrollback = await RunBlockScenarioAsync(PrintModeTestSupport.Environment());
        string noColorScrollback = await RunBlockScenarioAsync(EnvironmentWith("NO_COLOR", "1"));
        string asciiScrollback = await RunBlockScenarioAsync(EnvironmentWith("LUNATE_ASCII", "1"));

        Assert.Contains("extension saw the read", asciiScrollback, StringComparison.Ordinal);
        Assert.Equal(BlockText(defaultScrollback), BlockText(noColorScrollback));
        Assert.Equal(BlockText(defaultScrollback), BlockText(asciiScrollback));
        Assert.Contains("• first bullet", defaultScrollback, StringComparison.Ordinal);
        Assert.DoesNotContain("•", asciiScrollback, StringComparison.Ordinal);
        Assert.Contains("- first bullet", asciiScrollback, StringComparison.Ordinal);
        Assert.True(asciiScrollback.All(character => character <= 0x7F), asciiScrollback);
    }

    [Fact]
    public async Task A_failing_widget_is_omitted_logged_and_retried()
    {
        var log = new RecordingLog();
        var widgets = new StatusWidgetRegistry(log);
        var flaky = new FlakyWidget([Segment("flaky-widget", TextStyle.Warning)]);
        widgets.Register("ext.flaky", flaky);
        using var host = new InteractiveSessionHost(statusWidgets: widgets);
        host.Console.Size = new ConsoleSize(200, 24);
        host.Client.Enqueue(UsageUpdate(), Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Frames.Contains("0/128.0k", StringComparison.Ordinal);
        });
        Assert.DoesNotContain("flaky-widget", host.Frames, StringComparison.Ordinal);
        Assert.Equal(1, flaky.Calls);
        Assert.Contains(
            log.Messages,
            message =>
                message.StartsWith("error:", StringComparison.Ordinal)
                && message.Contains("ext.flaky", StringComparison.Ordinal)
        );

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Frames.Contains("flaky-widget", StringComparison.Ordinal);
        });

        Assert.Equal(2, flaky.Calls);
        Assert.Contains("15/128.0k", host.Frames, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    private static async Task<string> RunBlockScenarioAsync(Func<string, string?>? environment)
    {
        var renderers = new ToolResultRendererRegistry();
        renderers.Register(
            "ext.renderer",
            new FixedRenderer("read", [Segment("extension saw the read", TextStyle.Accent)])
        );
        using var host = new InteractiveSessionHost(
            toolResultRenderers: renderers,
            environment: environment
        );
        File.WriteAllText(host.Temp.File("a.txt"), "contents");
        host.Client.Enqueue(
            Scripts.Call("call-1", "read", Scripts.Args(("path", "a.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done\n\n- first bullet"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "extension saw the read");

        host.Console.Complete();
        await run;
        return host.ScrollbackText;
    }

    private static Func<string, string?> EnvironmentWith(string name, string value)
    {
        Func<string, string?> baseEnvironment = PrintModeTestSupport.Environment();
        return variable =>
            string.Equals(variable, name, StringComparison.Ordinal)
                ? value
                : baseEnvironment(variable);
    }

    private static string BlockText(string scrollback) =>
        scrollback
            .Split('\n')
            .Single(line => line.Contains("extension saw the read", StringComparison.Ordinal));

    private static ChatResponseUpdate UsageUpdate() =>
        new(
            ChatRole.Assistant,
            [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 })]
        );

    private static TextSegment Segment(string text, TextStyle style) => new(text, style);

    private sealed class RecordingLog : IExtensionLog
    {
        public List<string> Messages { get; } = [];

        public void Info(string message) => Messages.Add($"info: {message}");

        public void Warn(string message) => Messages.Add($"warn: {message}");

        public void Error(string message) => Messages.Add($"error: {message}");
    }

    private sealed class FixedWidget(IReadOnlyList<TextSegment> segments) : IStatusWidget
    {
        public IReadOnlyList<TextSegment> Segments(StatusView view) => segments;
    }

    private sealed class FlakyWidget(IReadOnlyList<TextSegment> segments) : IStatusWidget
    {
        public int Calls { get; private set; }

        public IReadOnlyList<TextSegment> Segments(StatusView view)
        {
            Calls++;
            return Calls == 1 ? throw new InvalidOperationException("widget broke") : segments;
        }
    }

    private sealed class FixedRenderer(string toolName, IReadOnlyList<TextSegment> segments)
        : IToolResultRenderer
    {
        public string ToolName => toolName;

        public IReadOnlyList<TextSegment> Render(ToolResultView view) => segments;
    }
}
