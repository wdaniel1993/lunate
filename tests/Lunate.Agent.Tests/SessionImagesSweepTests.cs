using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>The session-side sweep: what a successful load keeps and what it removes.</summary>
public sealed class SessionImagesSweepTests
{
    private static readonly byte[] PngBytes = SessionImageTestData.PngBytes;

    [Fact]
    public void Loading_a_session_removes_unreferenced_blobs_and_keeps_referenced_ones()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session session = Session.Create(
            path,
            "/work",
            new FixedTimeProvider(SessionImageTestData.Start)
        );
        ImageContentReference referenced = session.Images.Store(PngBytes, "image/png", 2, 3);
        session.AppendMessage(
            new ChatMessage(
                ChatRole.User,
                [new ImageContentReference(referenced.Digest, "image/png", 2, 3)]
            )
        );
        ImageContentReference orphan = session.Images.Store(
            new byte[] { 9, 8, 7, 6 },
            "image/jpeg",
            1,
            1
        );
        string imagesDirectory = session.Images.DirectoryPath!;
        string referencedPath = Path.Combine(imagesDirectory, $"{referenced.Digest}.png");
        string orphanPath = Path.Combine(imagesDirectory, $"{orphan.Digest}.jpg");

        Session loaded = Session.Load(path);

        Assert.True(File.Exists(referencedPath));
        Assert.False(File.Exists(orphanPath));
        Assert.True(loaded.Images.TryRead(referenced, out _));
    }

    [Fact]
    public void Loading_keeps_a_digest_nested_in_raw_preserved_message_json()
    {
        using var temp = new TempDirectory();
        string rawDigest = new('a', 64);
        string orphanDigest = new('b', 64);
        string path = temp.File("s_raw.jsonl");
        string messageLine =
            """{"type":"message","id":"e_01","parentId":null,"timestamp":"2026-10-06T12:00:01.0000000+00:00","message":{"role":"assistant","contents":[{"$type":"webSearchToolResult","callId":"call_w","result":[{"title":"Result","url":"https://example.com"}],"debug":{"nested":{"$type":"image","digest":"__DIGEST__","mime":"image/png","width":2,"height":3}}}]}}""".Replace(
                "__DIGEST__",
                rawDigest,
                StringComparison.Ordinal
            );
        File.WriteAllText(
            path,
            """{"type":"header","schema":3,"id":"s_raw","cwd":"/work","created":"2026-10-06T12:00:00.0000000+00:00","meai":"10.10.1"}"""
                + "\n"
                + messageLine
                + "\n"
        );
        string imagesDirectory = Path.Combine(temp.Root, "s_raw", "images");
        Directory.CreateDirectory(imagesDirectory);
        string rawPath = Path.Combine(imagesDirectory, $"{rawDigest}.png");
        string orphanPath = Path.Combine(imagesDirectory, $"{orphanDigest}.png");
        File.WriteAllBytes(rawPath, PngBytes);
        File.WriteAllBytes(orphanPath, PngBytes);

        Session loaded = Session.Load(path);

        SessionMessageEntry entry = Assert.IsType<SessionMessageEntry>(
            Assert.Single(loaded.Entries)
        );
        Assert.NotNull(entry.RawMessageJson);
        Assert.True(File.Exists(rawPath));
        Assert.False(File.Exists(orphanPath));
    }

    [Fact]
    public void Sweep_removes_only_old_temp_files_and_leaves_other_names_alone()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        string directory = session.Images.DirectoryPath!;
        string notes = Path.Combine(directory, "notes.txt");
        string fresh = Path.Combine(directory, ".tmp-fresh");
        string stale = Path.Combine(directory, ".tmp-stale");
        string uppercase = Path.Combine(directory, new string('A', 64) + ".png");
        File.WriteAllText(notes, "keep");
        File.WriteAllText(fresh, "fresh");
        File.WriteAllText(stale, "stale");
        File.WriteAllText(uppercase, "keep");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));

        session.Images.Sweep(new HashSet<string>(StringComparer.Ordinal));

        Assert.True(File.Exists(notes));
        Assert.True(File.Exists(fresh));
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(uppercase));
    }

    [Fact]
    public void Listing_a_sessions_directory_does_not_sweep_blobs()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        ImageContentReference orphan = session.Images.Store(
            new byte[] { 4, 5, 6 },
            "image/png",
            1,
            1
        );
        string orphanPath = Path.Combine(session.Images.DirectoryPath!, $"{orphan.Digest}.png");

        IReadOnlyList<SessionSummary> summaries = Session.List(temp.Root);

        Assert.Equal([session.SessionId], summaries.Select(summary => summary.Id));
        Assert.True(File.Exists(orphanPath));
    }

    [Fact]
    public void Compaction_keeps_the_blob_of_a_replaced_image_message()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session session = Session.Create(
            path,
            "/work",
            new FixedTimeProvider(SessionImageTestData.Start)
        );
        ImageContentReference image = session.Images.Store(PngBytes, "image/png", 2, 3);
        session.AppendMessage(
            new ChatMessage(
                ChatRole.User,
                [new ImageContentReference(image.Digest, "image/png", 2, 3)]
            )
        );
        string entryId = session.Entries[0].Id;
        session.AppendCompaction("summary", [entryId]);
        string blobPath = Path.Combine(session.Images.DirectoryPath!, $"{image.Digest}.png");

        Session loaded = Session.Load(path);

        Assert.True(File.Exists(blobPath));
        Assert.Equal("summary", loaded.ToHistory()[0].Text);
    }

    [Fact]
    public void Create_starts_with_an_empty_images_directory()
    {
        using var temp = new TempDirectory();
        string sessionId = "s_images";
        string imagesDirectory = Path.Combine(temp.Root, sessionId, "images");
        Directory.CreateDirectory(imagesDirectory);
        File.WriteAllBytes(
            Path.Combine(imagesDirectory, $"{SessionImageTestData.PngDigest}.png"),
            PngBytes
        );

        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            sessionId,
            new FixedTimeProvider(SessionImageTestData.Start)
        );

        Assert.Equal(sessionId, session.SessionId);
        Assert.True(Directory.Exists(imagesDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(imagesDirectory));
    }
}
