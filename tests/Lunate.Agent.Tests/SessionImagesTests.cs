using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>The content-addressed session image store: writes, reads and interrupted writes.</summary>
public sealed class SessionImagesTests
{
    private static readonly byte[] PngBytes = SessionImageTestData.PngBytes;

    [Fact]
    public void Identical_bytes_are_stored_once_under_the_digest_and_read_back()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);

        ImageContentReference first = session.Images.Store(PngBytes, "image/png", 2, 3);
        ImageContentReference second = session.Images.Store(PngBytes, "image/png", 2, 3);

        Assert.Equal(SessionImageTestData.PngDigest, first.Digest);
        Assert.Equal(SessionImageTestData.PngDigest, second.Digest);
        string imagesDirectory = Path.Combine(temp.Root, session.SessionId, "images");
        Assert.Equal(
            [Path.Combine(imagesDirectory, $"{SessionImageTestData.PngDigest}.png")],
            Directory.GetFiles(imagesDirectory)
        );
        Assert.True(session.Images.TryRead(first, out ReadOnlyMemory<byte> read));
        Assert.Equal(PngBytes, read.ToArray());
    }

    [Fact]
    public void Store_rejects_an_unsupported_media_type_naming_it()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            session.Images.Store(PngBytes, "image/tiff", 2, 3)
        );

        Assert.Contains("image/tiff", exception.Message);
    }

    [Fact]
    public void An_in_memory_store_stores_and_reads_without_touching_disk()
    {
        using var temp = new TempDirectory();
        SessionImages images = SessionImages.InMemory();

        ImageContentReference reference = images.Store(PngBytes, "image/png", 2, 3);
        bool read = images.TryRead(reference, out ReadOnlyMemory<byte> data);

        Assert.True(images.IsInMemory);
        Assert.Null(images.DirectoryPath);
        Assert.True(read);
        Assert.Equal(PngBytes, data.ToArray());
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Root));
    }

    [Fact]
    public void A_failed_write_leaves_no_temp_file_and_a_retry_succeeds()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        string imagesDirectory = session.Images.DirectoryPath!;
        string target = Path.Combine(imagesDirectory, $"{SessionImageTestData.PngDigest}.png");
        Directory.CreateDirectory(target);

        Assert.ThrowsAny<IOException>(() => session.Images.Store(PngBytes, "image/png", 2, 3));
        Assert.Empty(Directory.GetFiles(imagesDirectory, ".tmp-*"));
        Assert.True(Directory.Exists(target));

        Directory.Delete(target);
        ImageContentReference reference = session.Images.Store(PngBytes, "image/png", 2, 3);

        Assert.Equal(SessionImageTestData.PngDigest, reference.Digest);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void A_truncated_blob_at_the_target_is_replaced()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        string target = Path.Combine(
            session.Images.DirectoryPath!,
            $"{SessionImageTestData.PngDigest}.png"
        );
        File.WriteAllBytes(target, [1, 2, 3]);

        ImageContentReference reference = session.Images.Store(PngBytes, "image/png", 2, 3);

        Assert.Equal(PngBytes, File.ReadAllBytes(target));
        Assert.True(session.Images.TryRead(reference, out ReadOnlyMemory<byte> read));
        Assert.Equal(PngBytes, read.ToArray());
    }

    [Fact]
    public void A_blob_a_peer_completed_is_kept_instead_of_removed_as_unusable()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        ImageContentReference reference = session.Images.Store(PngBytes, "image/png", 2, 3);
        string target = Path.Combine(session.Images.DirectoryPath!, $"{reference.Digest}.png");

        // What a concurrent store of the same bytes does between this store's check and its remove: it
        // finishes the blob. The remove must keep it — deleting a completed blob leaves the peer that
        // renamed it to the target to find nothing there and fail with "already exists".
        SessionImages.RemoveUnusableBlob(target, PngBytes.Length);

        Assert.True(File.Exists(target));
        Assert.Equal(PngBytes, File.ReadAllBytes(target));
        Assert.True(session.Images.TryRead(reference, out ReadOnlyMemory<byte> read));
        Assert.Equal(PngBytes, read.ToArray());
    }

    [Fact]
    public void A_short_blob_is_removed_as_unusable()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        string target = Path.Combine(
            session.Images.DirectoryPath!,
            $"{SessionImageTestData.PngDigest}.png"
        );
        Directory.CreateDirectory(session.Images.DirectoryPath!);
        File.WriteAllBytes(target, [1, 2, 3]);

        SessionImages.RemoveUnusableBlob(target, PngBytes.Length);

        Assert.False(File.Exists(target));
    }

    [Fact]
    public void A_missing_blob_is_left_alone()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        string target = Path.Combine(
            session.Images.DirectoryPath!,
            $"{SessionImageTestData.PngDigest}.png"
        );

        SessionImages.RemoveUnusableBlob(target, PngBytes.Length);

        Assert.False(File.Exists(target));
    }

    [Fact]
    public void A_symlink_at_the_target_is_replaced_with_the_real_blob()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        string outside = temp.File("outside.png");
        File.WriteAllText(outside, "not the blob");
        string target = Path.Combine(
            session.Images.DirectoryPath!,
            $"{SessionImageTestData.PngDigest}.png"
        );
        File.CreateSymbolicLink(target, outside);

        ImageContentReference reference = session.Images.Store(PngBytes, "image/png", 2, 3);

        Assert.Null(new FileInfo(target).LinkTarget);
        Assert.Equal(PngBytes, File.ReadAllBytes(target));
        Assert.Equal("not the blob", File.ReadAllText(outside));
        Assert.True(session.Images.TryRead(reference, out ReadOnlyMemory<byte> read));
        Assert.Equal(PngBytes, read.ToArray());
    }

    [Fact]
    public async Task Concurrent_identical_stores_all_return_the_same_reference()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        byte[] bytes = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(bytes);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<ImageContentReference>[] stores =
        [
            .. Enumerable
                .Range(0, 8)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        await gate.Task;
                        return session.Images.Store(bytes, "image/png", 2, 3);
                    })
                ),
        ];
        gate.SetResult();
        ImageContentReference[] references = await Task.WhenAll(stores);

        Assert.All(references, reference => Assert.Equal(references[0].Digest, reference.Digest));
        string target = Path.Combine(session.Images.DirectoryPath!, $"{references[0].Digest}.png");
        Assert.Equal(bytes, File.ReadAllBytes(target));
        Assert.Empty(Directory.GetFiles(session.Images.DirectoryPath!, ".tmp-*"));
    }

    [Fact]
    public void An_oversized_blob_is_refused_instead_of_read()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        ImageContentReference reference = session.Images.Store(PngBytes, "image/png", 2, 3);
        string target = Path.Combine(session.Images.DirectoryPath!, $"{reference.Digest}.png");
        using (var stream = new FileStream(target, FileMode.Create, FileAccess.Write))
        {
            stream.SetLength(ImageLimits.MaxBytes + 1);
        }

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            session.Images.TryRead(reference, out _)
        );

        Assert.Contains(reference.Digest, exception.Message);
        Assert.Contains(
            ImageLimits.MaxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            exception.Message
        );
        Assert.Contains("corrupt", exception.Message);
    }

    [Fact]
    public void A_symlinked_blob_is_refused_instead_of_read()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        ImageContentReference reference = session.Images.Store(PngBytes, "image/png", 2, 3);
        string target = Path.Combine(session.Images.DirectoryPath!, $"{reference.Digest}.png");
        string outside = temp.File("outside.png");
        File.WriteAllText(outside, "outside");
        File.Delete(target);
        File.CreateSymbolicLink(target, outside);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            session.Images.TryRead(reference, out _)
        );

        Assert.Contains(reference.Digest, exception.Message);
        Assert.Contains("link", exception.Message);
    }

    [Fact]
    public void A_missing_blob_is_reported_with_its_digest_and_expected_path()
    {
        using var temp = new TempDirectory();
        Session session = SessionImageTestData.CreateSession(temp);
        ImageContentReference reference = session.Images.Store(PngBytes, "image/png", 2, 3);
        string expected = Path.Combine(session.Images.DirectoryPath!, $"{reference.Digest}.png");
        File.Delete(expected);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            session.Images.ValidateReferences([reference])
        );

        Assert.Contains(reference.Digest, exception.Message);
        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void An_in_memory_store_never_reports_a_missing_reference()
    {
        SessionImages images = SessionImages.InMemory();
        ImageContentReference reference = new(new string('c', 64), "image/png", 2, 3);

        images.ValidateReferences([reference]);
    }
}
