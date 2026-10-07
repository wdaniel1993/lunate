using System.Text;

namespace Lunate.Agent.Tests;

public sealed class SessionGoldenTests
{
    private static string SessionsDirectory =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "fixtures", "sessions");

    [Theory]
    [InlineData("golden-text.jsonl")]
    [InlineData("golden-tool-call.jsonl")]
    [InlineData("golden-mixed.jsonl")]
    [InlineData("golden-v2-header.jsonl")]
    [InlineData("golden-v2-entries.jsonl")]
    [InlineData("golden-v2-extension.jsonl")]
    [InlineData("golden-v2-unknown-content.jsonl")]
    public void Golden_sessions_round_trip_byte_for_byte(string name)
    {
        string path = Path.Combine(SessionsDirectory, name);
        string text = File.ReadAllText(path);
        string[] lines = text.Split('\n');
        Assert.Equal(string.Empty, lines[^1]);

        string roundTripped =
            string.Join(
                "\n",
                lines[..^1].Select(line => SessionFormat.Serialize(SessionFormat.Parse(line)))
            ) + "\n";

        Assert.Equal(File.ReadAllBytes(path), Encoding.UTF8.GetBytes(roundTripped));
    }

    [Theory]
    [InlineData("golden-text.jsonl")]
    [InlineData("golden-tool-call.jsonl")]
    [InlineData("golden-mixed.jsonl")]
    public void Schema_1_goldens_load_with_v1_semantics(string name)
    {
        string path = Path.Combine(SessionsDirectory, name);

        Session loaded = Session.Load(path);

        SessionHeaderEntry header = Assert.IsType<SessionHeaderEntry>(
            SessionFormat.Parse(File.ReadAllText(path).Split('\n')[0])
        );
        Assert.Equal(1, header.Schema);
        Assert.Null(header.Repo);
        Assert.Null(header.Worktree);
        Assert.All(
            loaded.Entries.OfType<SessionMessageEntry>(),
            entry => Assert.Null(entry.RawMessageJson)
        );
    }

    [Fact]
    public void The_unknown_content_golden_uses_the_raw_preservation_path()
    {
        string path = Path.Combine(SessionsDirectory, "golden-v2-unknown-content.jsonl");

        SessionMessageEntry message = Assert.IsType<SessionMessageEntry>(
            SessionFormat.Parse(File.ReadAllText(path).Split('\n')[1])
        );

        Assert.NotNull(message.RawMessageJson);
    }
}
