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
