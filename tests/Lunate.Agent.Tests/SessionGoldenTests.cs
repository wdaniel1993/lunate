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
}
