using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class UiRegistryTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void A_registered_renderer_renders_the_matching_tool_result()
    {
        var registry = new ToolResultRendererRegistry();
        registry.Register(
            "ext.a",
            new FixedRenderer("read", [new TextSegment("rendered", TextStyle.Dim)])
        );

        bool rendered = registry.TryRender(
            "read",
            View("read"),
            out IReadOnlyList<TextSegment> segments
        );

        Assert.True(rendered);
        Assert.Equal([new TextSegment("rendered", TextStyle.Dim)], segments);
    }

    [Fact]
    public void No_renderer_for_a_tool_is_not_a_match()
    {
        var registry = new ToolResultRendererRegistry();

        Assert.False(
            registry.TryRender("read", View("read"), out IReadOnlyList<TextSegment> segments)
        );
        Assert.Empty(segments);
    }

    [Fact]
    public void Duplicate_renderers_resolve_to_the_last_registration_and_log_the_conflict()
    {
        var log = new RecordingExtensionLog();
        var registry = new ToolResultRendererRegistry(log);
        registry.Register("ext.first", new FixedRenderer("read", [Segment("first")]));
        registry.Register("ext.second", new FixedRenderer("read", [Segment("second")]));

        bool rendered = registry.TryRender(
            "read",
            View("read"),
            out IReadOnlyList<TextSegment> segments
        );

        Assert.True(rendered);
        Assert.Equal("second", Assert.Single(segments).Text);
        string conflict = Assert.Single(
            log.Messages,
            message => message.Contains("replaces", StringComparison.Ordinal)
        );
        Assert.StartsWith("warn:", conflict, StringComparison.Ordinal);
        Assert.Contains("ext.first", conflict, StringComparison.Ordinal);
        Assert.Contains("ext.second", conflict, StringComparison.Ordinal);
    }

    [Fact]
    public void A_throwing_renderer_is_omitted_logged_and_retried()
    {
        var log = new RecordingExtensionLog();
        var registry = new ToolResultRendererRegistry(log);
        var renderer = new FlakyRenderer("read", failures: 1, [Segment("eventually")]);
        registry.Register("ext.a", renderer);

        Assert.False(
            registry.TryRender("read", View("read"), out IReadOnlyList<TextSegment> first)
        );
        Assert.Empty(first);
        Assert.Equal(1, renderer.Calls);
        Assert.Contains(
            log.Messages,
            message =>
                message.StartsWith("error:", StringComparison.Ordinal)
                && message.Contains("renderer", StringComparison.Ordinal)
                && message.Contains("ext.a", StringComparison.Ordinal)
        );

        Assert.True(
            registry.TryRender("read", View("read"), out IReadOnlyList<TextSegment> second)
        );
        Assert.Equal("eventually", Assert.Single(second).Text);
        Assert.Equal(2, renderer.Calls);
    }

    [Fact]
    public void Widgets_contribute_in_registration_order()
    {
        var registry = new StatusWidgetRegistry();
        registry.Register("ext.a", new FixedWidget([Segment("first")]));
        registry.Register("ext.b", new FixedWidget([Segment("second")]));

        Assert.Equal(
            ["first", "second"],
            registry.Segments(Status()).Select(segment => segment.Text)
        );
    }

    [Fact]
    public void A_throwing_widget_is_omitted_while_the_others_contribute_and_is_retried()
    {
        var log = new RecordingExtensionLog();
        var registry = new StatusWidgetRegistry(log);
        var flaky = new FlakyWidget(failures: 1, [Segment("later")]);
        registry.Register("ext.a", flaky);
        registry.Register("ext.b", new FixedWidget([Segment("steady")]));

        Assert.Equal(["steady"], registry.Segments(Status()).Select(segment => segment.Text));
        Assert.Contains(
            log.Messages,
            message =>
                message.StartsWith("error:", StringComparison.Ordinal)
                && message.Contains("widget", StringComparison.Ordinal)
                && message.Contains("ext.a", StringComparison.Ordinal)
        );

        Assert.Equal(
            ["later", "steady"],
            registry.Segments(Status()).Select(segment => segment.Text)
        );
        Assert.Equal(2, flaky.Calls);
    }

    [Fact]
    public void Unregister_removes_every_contribution_of_the_extension()
    {
        var renderers = new ToolResultRendererRegistry();
        renderers.Register("ext.a", new FixedRenderer("read", [Segment("a")]));
        renderers.Register("ext.b", new FixedRenderer("read", [Segment("b")]));
        var widgets = new StatusWidgetRegistry();
        widgets.Register("ext.a", new FixedWidget([Segment("a")]));
        widgets.Register("ext.b", new FixedWidget([Segment("b")]));

        renderers.Unregister("ext.a");
        widgets.Unregister("ext.a");

        Assert.True(
            renderers.TryRender("read", View("read"), out IReadOnlyList<TextSegment> segments)
        );
        Assert.Equal("b", Assert.Single(segments).Text);
        Assert.Equal("b", Assert.Single(widgets.Segments(Status())).Text);
    }

    [Fact]
    public async Task Extension_registrations_reach_the_loader_and_unload_removes_them()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("hello renderer rendered", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("hello widget contributed", StringComparison.Ordinal)
        );
        Assert.True(
            loader.ToolResultRenderers.TryRender(
                "hello-tool",
                View("hello-tool"),
                out IReadOnlyList<TextSegment> segments
            )
        );
        Assert.Equal("hello-rendered result", Assert.Single(segments).Text);
        Assert.Equal("hello-widget", Assert.Single(loader.StatusWidgets.Segments(Status())).Text);

        await loader.Unload("hello");

        Assert.False(
            loader.ToolResultRenderers.TryRender(
                "hello-tool",
                View("hello-tool"),
                out IReadOnlyList<TextSegment> _
            )
        );
        Assert.Empty(loader.StatusWidgets.Segments(Status()));
    }

    private static TextSegment Segment(string text) => new(text, TextStyle.Plain);

    private static ToolResultView View(string toolName) =>
        new(toolName, "call-1", "result", IsError: false);

    private static StatusView Status() => new("model", 1, 2, "/repo", "main");

    private sealed class FixedRenderer(string toolName, IReadOnlyList<TextSegment> segments)
        : IToolResultRenderer
    {
        public string ToolName => toolName;

        public IReadOnlyList<TextSegment> Render(ToolResultView view) => segments;
    }

    private sealed class FlakyRenderer(
        string toolName,
        int failures,
        IReadOnlyList<TextSegment> segments
    ) : IToolResultRenderer
    {
        public int Calls { get; private set; }

        public string ToolName => toolName;

        public IReadOnlyList<TextSegment> Render(ToolResultView view)
        {
            Calls++;
            if (Calls <= failures)
            {
                throw new InvalidOperationException("renderer broke");
            }

            return segments;
        }
    }

    private sealed class FixedWidget(IReadOnlyList<TextSegment> segments) : IStatusWidget
    {
        public IReadOnlyList<TextSegment> Segments(StatusView view) => segments;
    }

    private sealed class FlakyWidget(int failures, IReadOnlyList<TextSegment> segments)
        : IStatusWidget
    {
        public int Calls { get; private set; }

        public IReadOnlyList<TextSegment> Segments(StatusView view)
        {
            Calls++;
            if (Calls <= failures)
            {
                throw new InvalidOperationException("widget broke");
            }

            return segments;
        }
    }
}
