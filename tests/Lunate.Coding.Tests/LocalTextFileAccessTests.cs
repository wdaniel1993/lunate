using System.Text;

namespace Lunate.Coding.Tests;

public sealed class LocalTextFileAccessTests
{
    [Fact]
    public void ReadPrefix_returns_the_whole_small_file_with_its_length()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("small.txt"), "hello");

        (string Text, long Length)? probe = LocalTextFileAccess.Instance.ReadPrefix(
            temp.File("small.txt"),
            8192
        );

        Assert.NotNull(probe);
        Assert.Equal("hello", probe.Value.Text);
        Assert.Equal(5L, probe.Value.Length);
    }

    [Fact]
    public void ReadPrefix_returns_a_bounded_prefix_with_the_total_length()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("large.txt"), new string('a', 10_000));

        (string Text, long Length)? probe = LocalTextFileAccess.Instance.ReadPrefix(
            temp.File("large.txt"),
            8192
        );

        Assert.NotNull(probe);
        Assert.Equal(new string('a', 8192), probe.Value.Text);
        Assert.Equal(10_000L, probe.Value.Length);
    }

    [Fact]
    public void ReadPrefix_of_an_empty_file_is_empty_with_length_zero()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("empty.txt"), "");

        (string Text, long Length)? probe = LocalTextFileAccess.Instance.ReadPrefix(
            temp.File("empty.txt"),
            8192
        );

        Assert.NotNull(probe);
        Assert.Equal("", probe.Value.Text);
        Assert.Equal(0L, probe.Value.Length);
    }

    [Fact]
    public void ReadPrefix_strips_a_UTF8_BOM()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(
            temp.File("bom.txt"),
            [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("hi")]
        );

        (string Text, long Length)? probe = LocalTextFileAccess.Instance.ReadPrefix(
            temp.File("bom.txt"),
            8192
        );

        Assert.NotNull(probe);
        Assert.Equal("hi", probe.Value.Text);
        Assert.Equal(5L, probe.Value.Length);
    }
}
