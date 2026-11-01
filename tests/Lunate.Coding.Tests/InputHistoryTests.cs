namespace Lunate.Coding.Tests;

public sealed class InputHistoryTests
{
    [Fact]
    public void Multiline_entries_round_trip_through_the_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("history");
        var history = new InputHistory(path);

        history.Add("first line\nsecond line");
        history.Add("plain");

        string[] lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        var reloaded = new InputHistory(path);
        Assert.Equal(["first line\nsecond line", "plain"], reloaded.Entries);
    }

    [Fact]
    public void A_corrupt_line_is_skipped_on_load()
    {
        using var temp = new TempDirectory();
        string path = temp.File("history");
        File.WriteAllText(path, "not json\n\"valid\"\n{\"broken\":\n\"also valid\"\n");

        var history = new InputHistory(path);

        Assert.Equal(["valid", "also valid"], history.Entries);
    }

    [Fact]
    public void Up_walks_older_and_stays_at_the_oldest_entry()
    {
        using var temp = new TempDirectory();
        var history = new InputHistory(temp.File("history"));
        history.Add("one");
        history.Add("two");
        history.Add("three");

        Assert.True(history.TryPrevious(out string third));
        Assert.Equal("three", third);
        Assert.True(history.TryPrevious(out string second));
        Assert.Equal("two", second);
        Assert.True(history.TryPrevious(out string first));
        Assert.Equal("one", first);
        Assert.False(history.TryPrevious(out _));
    }

    [Fact]
    public void Down_walks_newer_and_reports_when_the_walk_leaves_the_history()
    {
        using var temp = new TempDirectory();
        var history = new InputHistory(temp.File("history"));
        history.Add("one");
        history.Add("two");
        history.TryPrevious(out _);
        history.TryPrevious(out _);

        Assert.True(history.TryNext(out string second));
        Assert.Equal("two", second);
        Assert.False(history.TryNext(out _));
        Assert.False(history.TryNext(out _));
    }

    [Fact]
    public void Adding_an_entry_resets_navigation()
    {
        using var temp = new TempDirectory();
        var history = new InputHistory(temp.File("history"));
        history.Add("one");
        history.Add("two");
        history.TryPrevious(out _);

        history.Add("three");

        Assert.True(history.TryPrevious(out string newest));
        Assert.Equal("three", newest);
    }

    [Fact]
    public void Blank_input_is_not_stored()
    {
        using var temp = new TempDirectory();
        string path = temp.File("history");
        var history = new InputHistory(path);

        history.Add("  \n ");

        Assert.Empty(history.Entries);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void The_history_file_and_a_fresh_store_directory_are_owner_only_on_posix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not exist on Windows.");
            return;
        }

        using var temp = new TempDirectory();
        string path = temp.File("store/history");
        var history = new InputHistory(path);

        history.Add("prompt");

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(path)!)
        );
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void An_existing_world_readable_history_file_is_narrowed_on_the_next_write()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not exist on Windows.");
            return;
        }

        using var temp = new TempDirectory();
        string path = temp.File("history");
        File.WriteAllText(path, "\"one\"\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead
                | UnixFileMode.OtherRead
        );
        var history = new InputHistory(path);

        history.Add("two");

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }
}
