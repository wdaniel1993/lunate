using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace UiShowcaseExtension.Tests;

/// <summary>
/// The sample through the real loader: the built extension installs into a temp store, loads
/// through the production discovery/trust path, and its registered tool, renderer and widget are
/// driven from the registries — never from a hand-constructed instance.
/// </summary>
public sealed class UiShowcaseExtensionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string RawResult = """{"values":[3,7,1,9]}""";

    [Fact]
    public async Task The_loader_registers_the_read_only_tool_the_renderer_and_the_widget()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);
        await host.StartAsync(Ct);

        ITool tool = Assert.Single(
            host.Runner.Tools.List(),
            candidate => candidate.Name == "uikit__bars"
        );
        Assert.Equal(ToolRisk.ReadOnly, tool.Risk);
        Assert.True(
            host.Runner.ToolResultRenderers.TryRender(
                "uikit__bars",
                View(RawResult),
                out IReadOnlyList<TextSegment> segments
            ),
            "the bars renderer must be registered for the model-facing tool name"
        );
        Assert.NotEmpty(segments);
        Assert.NotEmpty(host.Runner.StatusWidgets.Segments(Status(tokens: 25, window: 100)));
    }

    [Fact]
    public async Task The_renderer_draws_one_accent_bar_line_per_value()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);
        await host.StartAsync(Ct);

        Assert.True(
            host.Runner.ToolResultRenderers.TryRender(
                "uikit__bars",
                View(RawResult),
                out IReadOnlyList<TextSegment> segments
            )
        );

        Assert.Equal(
            [
                new TextSegment("███ ", TextStyle.Accent),
                new TextSegment("3\n", TextStyle.Dim),
                new TextSegment("███████ ", TextStyle.Accent),
                new TextSegment("7\n", TextStyle.Dim),
                new TextSegment("█ ", TextStyle.Accent),
                new TextSegment("1\n", TextStyle.Dim),
                new TextSegment("█████████ ", TextStyle.Accent),
                new TextSegment("9", TextStyle.Dim),
            ],
            segments
        );
    }

    [Fact]
    public async Task Garbage_and_empty_results_render_nothing()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);
        await host.StartAsync(Ct);

        foreach (
            string text in new[]
            {
                string.Empty,
                "   ",
                "not json",
                """{"values":["x"]}""",
                """{"values":[1.5]}""",
                """{"other":[1]}""",
                "[3,7]",
            }
        )
        {
            Assert.True(
                host.Runner.ToolResultRenderers.TryRender(
                    "uikit__bars",
                    View(text),
                    out IReadOnlyList<TextSegment> segments
                ),
                "the registration must match; only the contribution degrades"
            );
            Assert.Empty(segments);
        }
    }

    [Fact]
    public async Task The_bar_glyph_cap_holds_and_the_true_value_stays_visible()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);
        await host.StartAsync(Ct);

        Assert.True(
            host.Runner.ToolResultRenderers.TryRender(
                "uikit__bars",
                View("""{"values":[100]}"""),
                out IReadOnlyList<TextSegment> segments
            )
        );

        Assert.Equal(
            [
                new TextSegment(new string('█', 40) + " ", TextStyle.Accent),
                new TextSegment("100", TextStyle.Dim),
            ],
            segments
        );
    }

    [Fact]
    public async Task The_widget_shows_low_usage_in_accent()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);
        await host.StartAsync(Ct);

        Assert.Equal(
            [new TextSegment("▰▰▱▱▱▱▱▱ 25%", TextStyle.Accent)],
            host.Runner.StatusWidgets.Segments(Status(tokens: 25, window: 100))
        );
    }

    [Fact]
    public async Task The_widget_warns_at_eighty_percent_and_above()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);
        await host.StartAsync(Ct);

        Assert.Equal(
            [new TextSegment("▰▰▰▰▰▰▱▱ 80%", TextStyle.Warning)],
            host.Runner.StatusWidgets.Segments(Status(tokens: 80, window: 100))
        );
        Assert.Equal(
            [new TextSegment("▰▰▰▰▰▰▰▰ 100%", TextStyle.Warning)],
            host.Runner.StatusWidgets.Segments(Status(tokens: 100, window: 100))
        );
    }

    [Fact]
    public async Task The_widget_reports_an_unknown_window()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);
        await host.StartAsync(Ct);

        Assert.Equal(
            [new TextSegment("ctx n/a", TextStyle.Dim)],
            host.Runner.StatusWidgets.Segments(Status(tokens: 7, window: 0))
        );
    }

    [Fact]
    public async Task The_produced_segments_round_trip_through_the_ai_json_options()
    {
        using var temp = new TempDirectory();
        await using var host = CreateHost(temp);
        await host.StartAsync(Ct);

        Assert.True(
            host.Runner.ToolResultRenderers.TryRender(
                "uikit__bars",
                View(RawResult),
                out IReadOnlyList<TextSegment> rendered
            )
        );
        IReadOnlyList<TextSegment> produced =
        [
            .. rendered,
            .. host.Runner.StatusWidgets.Segments(Status(tokens: 25, window: 100)),
        ];

        string json = JsonSerializer.Serialize(produced, AIJsonUtilities.DefaultOptions);
        List<TextSegment> roundTripped = JsonSerializer.Deserialize<List<TextSegment>>(
            json,
            AIJsonUtilities.DefaultOptions
        )!;

        Assert.Equal(produced, roundTripped);
    }

    private static ExtensionTestHost CreateHost(TempDirectory temp) =>
        new(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "uikit",
                ExtensionDirectory = Path.Combine(AppContext.BaseDirectory, "UiShowcaseExtension"),
            }
        );

    private static ToolResultView View(string resultText) =>
        new("uikit__bars", "call-1", resultText, IsError: false);

    private static StatusView Status(long tokens, long window) =>
        new("gpt-4o-mini", tokens, window, "/repo", "main");
}
