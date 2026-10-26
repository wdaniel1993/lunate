namespace Lunate.Coding.Tests;

/// <summary>Resuming a parent session surfaces its child-session link as one informational line.</summary>
public sealed class InteractiveSessionResumeSubagentTests
{
    [Fact]
    public async Task Resuming_a_parent_renders_its_child_link_line()
    {
        using var host = new InteractiveSessionHost();
        string directory = host.Temp.File("sessions");
        Directory.CreateDirectory(directory);
        string fixtures = Path.Combine(RepositoryRoot(), "tests", "fixtures", "sessions");
        string parentPath = Path.Combine(directory, "s_20261013-090000-a1b2.jsonl");
        string childPath = Path.Combine(directory, "s_20261013-090100-c3d4.jsonl");
        File.Copy(Path.Combine(fixtures, "s_20261013-090000-a1b2.jsonl"), parentPath);
        File.Copy(Path.Combine(fixtures, "s_20261013-090100-c3d4.jsonl"), childPath);
        File.SetLastWriteTimeUtc(childPath, new DateTime(2026, 10, 13, 9, 1, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(parentPath, new DateTime(2026, 10, 13, 9, 2, 0, DateTimeKind.Utc));
        Task run = host.RunAsync();

        host.Console.SendText("/resume");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select session");
        host.Console.Send(
            new Lunate.Tui.KeyEvent(Lunate.Tui.KeyKind.Down, null, false, false, false)
        );
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "child session: s_20261013-090100-c3d4 (run run_20261013-090100-c3d4)"
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static string RepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find lunate.slnx above {AppContext.BaseDirectory}."
        );
    }
}
