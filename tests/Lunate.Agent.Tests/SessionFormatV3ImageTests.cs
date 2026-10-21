using System.Text;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>Schema 3 image reference parts: exact bytes, typed loads and unknown-type rejection.</summary>
public sealed class SessionFormatV3ImageTests
{
    private const string Digest =
        "3c6873f6791287511b4fc539436471e02378c90150f8e6c818746023520e6a49";

    private static readonly DateTimeOffset Recorded = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static string SessionsDirectory =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "fixtures", "sessions");

    [Fact]
    public void An_image_reference_part_round_trips_byte_for_byte()
    {
        var entry = new SessionMessageEntry(
            "e_01",
            null,
            Recorded,
            new ChatMessage(
                ChatRole.User,
                [
                    new TextContent("What is wrong? [image 1024x768]"),
                    new ImageContentReference(Digest, "image/png", 1024, 768),
                ]
            ),
            null,
            null
        );

        string line = SessionFormat.Serialize(entry);

        Assert.Equal(
            $$$"""{"type":"message","id":"e_01","parentId":null,"timestamp":"2026-10-06T12:00:00.0000000+00:00","message":{"role":"user","contents":[{"$type":"text","text":"What is wrong? [image 1024x768]"},{"$type":"image","digest":"{{{Digest}}}","mime":"image/png","width":1024,"height":768}]}}""",
            line
        );

        SessionMessageEntry parsed = Assert.IsType<SessionMessageEntry>(SessionFormat.Parse(line));

        ImageContentReference image = Assert.IsType<ImageContentReference>(
            parsed.Message.Contents[1]
        );
        Assert.Equal(Digest, image.Digest);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(1024, image.Width);
        Assert.Equal(768, image.Height);
        Assert.Null(parsed.RawMessageJson);
        Assert.Equal(line, SessionFormat.Serialize(parsed));
    }

    [Fact]
    public void Loading_the_image_golden_yields_a_typed_reference()
    {
        Session loaded = Session.Load(Path.Combine(SessionsDirectory, "golden-v3-image.jsonl"));

        SessionMessageEntry user = Assert.Single(
            loaded.Entries.OfType<SessionMessageEntry>(),
            entry => entry.Message.Role == ChatRole.User
        );
        ImageContentReference image = Assert.IsType<ImageContentReference>(
            user.Message.Contents[1]
        );
        Assert.Equal(Digest, image.Digest);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(1024, image.Width);
        Assert.Equal(768, image.Height);
        Assert.Null(user.RawMessageJson);
    }

    [Fact]
    public void Load_rejects_an_unknown_content_type_naming_the_file_and_line()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        File.WriteAllText(
            path,
            """{"type":"header","schema":3,"id":"s_x","cwd":"/work","created":"2026-10-06T12:00:00.0000000+00:00","meai":"10.10.1"}"""
                + "\n"
                + """{"type":"message","id":"e_01","parentId":null,"timestamp":"2026-10-06T12:00:01.0000000+00:00","message":{"role":"user","contents":[{"$type":"quantumImage","digest":"3c6873f6791287511b4fc539436471e02378c90150f8e6c818746023520e6a49","mime":"image/png","width":1024,"height":768}]}}"""
                + "\n"
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Session.Load(path)
        );

        Assert.Contains(path, exception.Message);
        Assert.Contains("line 2", exception.Message);
    }

    [Fact]
    public void Every_pre_existing_golden_stays_byte_identical()
    {
        string[] paths = Directory.GetFiles(SessionsDirectory, "golden-*.jsonl");
        Assert.NotEmpty(paths);

        foreach (string path in paths)
        {
            if (Path.GetFileName(path) == "golden-v3-image.jsonl")
            {
                continue;
            }

            string text = File.ReadAllText(path);
            string[] lines = text.Split('\n');
            Assert.Equal(string.Empty, lines[^1]);

            string roundTripped =
                string.Join(
                    "\n",
                    lines[..^1].Select(line => SessionFormat.Serialize(SessionFormat.Parse(line)))
                ) + "\n";

            Assert.True(
                File.ReadAllBytes(path).SequenceEqual(Encoding.UTF8.GetBytes(roundTripped)),
                $"{Path.GetFileName(path)} no longer round-trips byte for byte."
            );
        }
    }

    [Fact]
    public void A_schema_2_golden_still_loads()
    {
        Session loaded = Session.Load(Path.Combine(SessionsDirectory, "golden-v2-header.jsonl"));

        Assert.Equal(2, loaded.Schema);
    }
}
