using System.Text.Json;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>
/// Recording and replaying a session whose history hydrates an image: the fixture pins the image
/// digest, so the replayed request must hash the hydrated bytes exactly like the live turn did.
/// </summary>
public sealed class AgentImageRecordingTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_replayed_session_with_an_image_returns_the_recorded_response()
    {
        using var temp = new TempDirectory();
        Session live = await SeedImageTurnAsync(
            temp.File("live.jsonl"),
            "s_live",
            SessionImageTestData.PngBytes
        );
        string replayPath = Snapshot(live, temp, "replay");
        string fixture = temp.File("vision.jsonl");

        await RunAsync(CreateRecorder(fixture), live, "again");

        string fixtureText = File.ReadAllText(fixture);
        Assert.DoesNotContain(
            Convert.ToBase64String(SessionImageTestData.PngBytes),
            fixtureText,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("data:image/png;base64", fixtureText, StringComparison.Ordinal);

        Session loaded = Session.Load(replayPath);
        List<AgentEvent> replayed = await RunAsync(new ReplayChatClient(fixture), loaded, "again");

        Assert.DoesNotContain(replayed, agentEvent => agentEvent is RunError);
        Assert.Contains(replayed, agentEvent => agentEvent is RunFinished);
        Assert.Equal(
            "recorded reply",
            string.Concat(replayed.OfType<TextMessageContent>().Select(content => content.Text))
        );
    }

    [Fact]
    public async Task A_replayed_session_with_different_image_pixels_fails_with_a_digest_mismatch()
    {
        using var temp = new TempDirectory();
        Session recorded = await SeedImageTurnAsync(
            temp.File("recorded.jsonl"),
            "s_recorded",
            SessionImageTestData.PngBytes
        );
        string fixture = temp.File("vision.jsonl");
        await RunAsync(CreateRecorder(fixture), recorded, "again");

        byte[] changedPixels = [.. SessionImageTestData.PngBytes];
        changedPixels[8] = 0x7F;
        Session changed = await SeedImageTurnAsync(
            temp.File("changed.jsonl"),
            "s_changed",
            changedPixels
        );

        List<AgentEvent> replayed = await RunAsync(new ReplayChatClient(fixture), changed, "again");

        RunError error = Assert.IsType<RunError>(Assert.Single(replayed.OfType<RunError>()));
        Assert.Contains(fixture, error.Message, StringComparison.Ordinal);
        Assert.Contains("mismatch", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exchange 1", error.Message, StringComparison.Ordinal);
        Assert.Contains(RecordedDigest(fixture)[..12], error.Message, StringComparison.Ordinal);
        Assert.Contains("but got", error.Message, StringComparison.Ordinal);
    }

    private static async Task<Session> SeedImageTurnAsync(
        string path,
        string sessionId,
        byte[] image
    )
    {
        Session session = Session.Create(
            path,
            "/work",
            sessionId,
            new FixedTimeProvider(SessionImageTestData.Start)
        );
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("seen"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = session }
        );

        await harness
            .RunAsync(new UserInput("look", [new ImageAttachment("image/png", 2, 3, image)]), Ct)
            .ToListAsync(Ct);
        return session;
    }

    private static RecordingChatClient CreateRecorder(string fixture) =>
        new(
            new ScriptedChatClient().Enqueue(
                LoopScripts.Text("recorded reply"),
                LoopScripts.Stop()
            ),
            fixture,
            "vision-test"
        );

    private static async Task<List<AgentEvent>> RunAsync(
        IChatClient client,
        Session session,
        string text
    )
    {
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = session }
        );
        return await harness.RunAsync(text, Ct).ToListAsync(Ct);
    }

    /// <summary>Copies the session and its image blobs before the recording turn extends it.</summary>
    private static string Snapshot(Session session, TempDirectory temp, string name)
    {
        string directory = temp.File(name);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Path.GetFileName(session.Path));
        File.Copy(session.Path, path);

        string images = Path.Combine(
            Path.GetDirectoryName(session.Path)!,
            session.SessionId,
            "images"
        );
        if (!Directory.Exists(images))
        {
            return path;
        }

        string target = Path.Combine(directory, session.SessionId, "images");
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(images))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        return path;
    }

    private static string RecordedDigest(string fixture)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllLines(fixture)[1]);
        return document.RootElement.GetProperty("requestDigest").GetString()
            ?? throw new InvalidDataException($"Fixture '{fixture}' has no requestDigest.");
    }
}
