using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The approval panel's snapshot matrix (change <c>approval-panel</c>, task 3.2): a bash command,
/// an edit with its diff, a read, an unknown tool's raw arguments and a subagent asker — each at
/// 40×20, 60×30, 80×24 and 120×40 in every colour mode the capability helper selects. The models
/// carry fixed detail lines, so the goldens are clock- and I/O-free. Regenerate deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet run --project tests/Lunate.Tui.Tests/Lunate.Tui.Tests.csproj -- -class "Lunate.Tui.Tests.ApprovalFrameGoldenTests"</c>.
/// </summary>
public sealed class ApprovalFrameGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "frames",
            "approval"
        );

    private static readonly (int Width, int Height)[] Sizes =
    [
        (40, 20),
        (60, 30),
        (80, 24),
        (120, 40),
    ];

    private static readonly string[] Modes = ["truecolor", "256-color", "16-color", "no-color"];

    private static readonly string[] Shapes = ["bash", "edit", "read", "unknown", "subagent"];

    public static TheoryData<string, int, int, string> Cases
    {
        get
        {
            var cases = new TheoryData<string, int, int, string>();
            foreach (string shape in Shapes)
            {
                foreach ((int width, int height) in Sizes)
                {
                    foreach (string mode in Modes)
                    {
                        cases.Add(shape, width, height, mode);
                    }
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_approval_frame_matches_the_committed_golden(
        string shape,
        int width,
        int height,
        string mode
    )
    {
        string[] lines = [.. LiveAreaRenderer.Render(State(shape, width, height, mode)).Lines];

        Assert.True(
            lines.Length <= height,
            $"{shape} at {width}x{height} renders {lines.Length} rows"
        );
        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"{shape}-{width}x{height}-{mode}.txt"),
            [.. lines.Select(Escape)]
        );
    }

    private static LiveAreaState State(string shape, int width, int height, string mode) =>
        new()
        {
            Approval = Model(shape),
            Color = Capability(mode),
            Unicode = true,
            Footer = new StatusFooterModel(
                "deepseek-v4.1-flash",
                2100,
                1_000_000,
                "~/dev/lunate",
                "main"
            ),
            Size = new ConsoleSize(width, height),
        };

    private static ApprovalPromptModel Model(string shape) =>
        shape switch
        {
            "bash" => new ApprovalPromptModel(
                "bash",
                "{}",
                Detail:
                [
                    "dotnet test --project tests/Lunate.Coding.Tests --filter Approval",
                    "Test run summary: Passed!",
                    "  total: 42",
                    "  failed: 0",
                    "  succeeded: 42",
                    "  skipped: 0",
                ]
            ),
            "edit" => new ApprovalPromptModel(
                "edit",
                "{}",
                Detail:
                [
                    "src/Lunate.Coding/ApprovalDetail.cs  +2 \u22121",
                    "@@ -10,4 +10,5 @@",
                    " public static IReadOnlyList<string> Build(",
                    "     string toolName,",
                    "-    JsonElement args",
                    "+    JsonElement args,",
                    "+    bool unicode",
                ]
            ),
            "read" => new ApprovalPromptModel(
                "read",
                "{}",
                Detail: ["src/Lunate.Tui/Interaction/ApprovalPanelLayout.cs (10-14)"]
            ),
            "unknown" => new ApprovalPromptModel(
                "unknown",
                "{}",
                Detail:
                [
                    "{",
                    "  \"alpha\": 1,",
                    "  \"beta\": 2,",
                    "  \"gamma\": 3,",
                    "  \"delta\": 4,",
                    "  \"epsilon\": 5,",
                    "  ... +3 lines",
                ]
            ),
            "subagent" => new ApprovalPromptModel(
                "bash",
                "{}",
                Asker: "subagent (depth 1)",
                Detail: ["dotnet format --verify-no-changes", "Formatting check passed."]
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown shape."),
        };

    /// <summary>The capability from the detection seam; Detect is the 16-colour bucket.</summary>
    private static ColorSystemSupport Capability(string mode) =>
        mode switch
        {
            "truecolor" => ColorSystemSupport.TrueColor,
            "256-color" => ColorSystemSupport.EightBit,
            "16-color" => ColorSystemSupport.Detect,
            _ => ColorSystemSupport.NoColors,
        };

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\")
            .Replace("\u001b", "\\x1b")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
}
