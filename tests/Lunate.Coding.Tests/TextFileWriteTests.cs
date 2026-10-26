using System.Text;

namespace Lunate.Coding.Tests;

/// <summary>
/// <see cref="TextFile.WriteRaw"/> never truncates the destination: it writes beside it and replaces
/// in one step, keeps the original's mode and attributes, and writes through a symlink instead of
/// replacing the link. The write side of the write and edit tools depends on all three.
/// </summary>
public sealed class TextFileWriteTests
{
    [Fact]
    public void A_write_that_cannot_replace_the_destination_leaves_the_original_bytes_unchanged()
    {
        using var temp = new TempDirectory();
        string directory = temp.File("directory");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "File.txt");
        File.WriteAllText(path, "original\n");
        byte[] before = File.ReadAllBytes(path);

        // POSIX: a directory without the write bit cannot hold the temporary file, so the write must
        // fail. Windows: an exclusively held destination makes the replace fail; the platform may
        // also let it through, so the assertion is the invariant both outcomes share — a failed
        // write leaves the original bytes alone, and a successful one writes the new text.
        UnixFileMode? originalMode = null;
        FileStream? holder = null;
        if (OperatingSystem.IsWindows())
        {
            holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        else
        {
            originalMode = File.GetUnixFileMode(directory);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        try
        {
            var failed = false;
            try
            {
                TextFile.WriteRaw(path, "replacement\n", hasBom: false);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                failed = true;
            }

            if (failed)
            {
                Assert.Equal(before, File.ReadAllBytes(path));
            }
            else
            {
                Assert.Equal(Encoding.UTF8.GetBytes("replacement\n"), File.ReadAllBytes(path));
            }

            if (!OperatingSystem.IsWindows())
            {
                Assert.True(failed, "a directory without the write bit must refuse the write");
            }
        }
        finally
        {
            holder?.Dispose();
            if (!OperatingSystem.IsWindows() && originalMode is { } mode)
            {
                File.SetUnixFileMode(directory, mode);
            }
        }
    }

    [Fact]
    public void A_rewrite_keeps_the_original_unix_mode()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not exist on Windows.");
            return;
        }

        using var temp = new TempDirectory();
        string path = temp.File("Script.sh");
        File.WriteAllText(path, "old\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead
        );

        TextFile.WriteRaw(path, "new\n", hasBom: false);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
            File.GetUnixFileMode(path)
        );
        Assert.Equal("new\n", File.ReadAllText(path));
        Assert.Equal(
            [Path.GetFileName(path)],
            Directory.GetFiles(temp.Root).Select(Path.GetFileName)
        );
    }

    [Fact]
    public void Writing_through_a_symlink_updates_the_target_and_keeps_the_link()
    {
        using var temp = new TempDirectory();
        string target = temp.File("Target.txt");
        string link = temp.File("Link.txt");
        File.WriteAllText(target, "old\n");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Symbolic links are not available in this environment.");
            return;
        }

        TextFile.WriteRaw(link, "new\n", hasBom: false);

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("new\n", File.ReadAllText(target));
        Assert.Equal("new\n", File.ReadAllText(link));
    }

    [Fact]
    public void The_byte_order_mark_is_written_when_asked_and_absent_otherwise()
    {
        using var temp = new TempDirectory();
        string path = temp.File("Bom.txt");

        TextFile.WriteRaw(path, "grüße\n", hasBom: true);
        (string text, bool hasBom) = TextFile.ReadRaw(path);
        Assert.True(hasBom);
        Assert.Equal("grüße\n", text);
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(path)[..3]);

        TextFile.WriteRaw(path, "grüße\n", hasBom: false);
        (text, hasBom) = TextFile.ReadRaw(path);
        Assert.False(hasBom);
        Assert.Equal("grüße\n", text);
    }
}
