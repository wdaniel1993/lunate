namespace Lunate.Tui.Tests;

/// <summary>
/// The follow-up queue indicator beside the status footer: zero hides it, one and five show the
/// count, and the footer's mode hint survives at 40 columns. Regenerate deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests</c>.
/// </summary>
public sealed class FollowUpIndicatorGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "follow-up"
        );

    private static readonly int[] Widths = [40, 80, 120];

    private static readonly int[] Counts = [0, 1, 5];

    public static TheoryData<int, int> Cases
    {
        get
        {
            var cases = new TheoryData<int, int>();
            foreach (int width in Widths)
            {
                foreach (int count in Counts)
                {
                    cases.Add(width, count);
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_indicator_matches_the_committed_golden(int width, int count)
    {
        string[] lines = Render(width, count).Lines.Select(Escape).ToArray();

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"queued-{count}-{width}.txt"),
            lines
        );
    }

    [Fact]
    public void Zero_queued_renders_no_indicator()
    {
        string frame = string.Join('\n', Render(width: 80, count: 0).Lines);

        Assert.DoesNotContain("queued", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void A_queued_message_shows_the_count_beside_the_footer_hint()
    {
        RenderedFrame frame = Render(width: 80, count: 5);

        Assert.Contains("5 queued", frame.Lines[^1], StringComparison.Ordinal);
        Assert.Contains("deepseek-v4.1-flash", frame.Lines[^1], StringComparison.Ordinal);
    }

    private static RenderedFrame Render(int width, int count) =>
        LiveAreaRenderer.Render(
            new LiveAreaState
            {
                QueuedCount = count,
                Footer = new StatusFooterModel(
                    "deepseek-v4.1-flash",
                    2100,
                    128000,
                    "~/dev/lunate",
                    "main"
                ),
                Size = new ConsoleSize(width, 24),
            }
        );

    private static string Escape(string text) => text.Replace("\u001b", "\\x1b");
}
