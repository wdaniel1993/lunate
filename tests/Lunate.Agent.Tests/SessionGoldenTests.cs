using System.Text;
using Microsoft.Extensions.AI;

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
    [InlineData("golden-reasoning.jsonl")]
    [InlineData("golden-v3-usage.jsonl")]
    [InlineData("golden-v3-image.jsonl")]
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
    public void The_usage_golden_records_all_four_token_lanes()
    {
        string path = Path.Combine(SessionsDirectory, "golden-v3-usage.jsonl");

        Session loaded = Session.Load(path);

        Assert.Equal(3, loaded.Schema);
        SessionMessageEntry assistant = Assert.Single(
            loaded.Entries.OfType<SessionMessageEntry>(),
            entry => entry.Message.Role == ChatRole.Assistant
        );
        Assert.Equal(new SessionUsage(1500, 50, 1200, 300), assistant.Usage);
    }

    [Fact]
    public void The_image_golden_loads_as_schema_3_with_a_typed_reference()
    {
        string path = Path.Combine(SessionsDirectory, "golden-v3-image.jsonl");

        Session loaded = Session.Load(path);

        Assert.Equal(3, loaded.Schema);
        SessionMessageEntry user = Assert.Single(
            loaded.Entries.OfType<SessionMessageEntry>(),
            entry => entry.Message.Role == ChatRole.User
        );
        ImageContentReference image = Assert.IsType<ImageContentReference>(
            user.Message.Contents[1]
        );
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(1024, image.Width);
        Assert.Equal(768, image.Height);
    }

    [Fact]
    public void The_reasoning_golden_keeps_protected_data()
    {
        string path = Path.Combine(SessionsDirectory, "golden-reasoning.jsonl");

        Session loaded = Session.Load(path);

        SessionMessageEntry assistant = Assert.Single(
            loaded.Entries.OfType<SessionMessageEntry>(),
            entry => entry.Message.Role == ChatRole.Assistant
        );
        TextReasoningContent reasoning = Assert.IsType<TextReasoningContent>(
            assistant.Message.Contents[0]
        );
        Assert.Equal("Rayleigh scattering makes shorter wavelengths scatter more.", reasoning.Text);
        Assert.Equal(
            "EqQBCgIYAhIM1gbcDa9GJwZA2b3hGgxBdjrkzLoky3dl1pkiMOYdsM7vWc5jJ0r8uKfXyQ2n4hT1bV6mR9sP0aL3zC8eD5gF7iH2jK4lN6oQ",
            reasoning.ProtectedData
        );
        Assert.Equal("claude-sonnet-4-5", assistant.Model);
        Assert.Null(assistant.RawMessageJson);
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
