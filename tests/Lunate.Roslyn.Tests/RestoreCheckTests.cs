using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class RestoreCheckTests
{
    [Fact]
    public void A_project_without_assets_is_reported_with_its_directory()
    {
        using var temp = new TempDirectory();
        var projectDir = temp.Subdirectory("App");
        var project = Path.Combine(projectDir, "App.csproj");
        System.IO.File.WriteAllText(project, "");

        var missing = RestoreCheck.FindMissing([project]);

        Assert.Equal([projectDir], missing);
    }

    [Fact]
    public void A_project_with_assets_is_not_reported()
    {
        using var temp = new TempDirectory();
        var projectDir = temp.Subdirectory("App");
        var project = Path.Combine(projectDir, "App.csproj");
        System.IO.File.WriteAllText(project, "");
        var obj = Path.Combine(projectDir, "obj");
        System.IO.Directory.CreateDirectory(obj);
        System.IO.File.WriteAllText(Path.Combine(obj, "project.assets.json"), "{}");

        var missing = RestoreCheck.FindMissing([project]);

        Assert.Empty(missing);
    }

    [Fact]
    public void Missing_directories_are_deduplicated_and_ordered()
    {
        using var temp = new TempDirectory();
        var app = Path.Combine(temp.Subdirectory("App"), "App.csproj");
        var lib = Path.Combine(temp.Subdirectory("Lib"), "Lib.csproj");
        System.IO.File.WriteAllText(app, "");
        System.IO.File.WriteAllText(lib, "");

        var missing = RestoreCheck.FindMissing([app, app, lib]);

        Assert.Equal(2, missing.Count);
    }

    [Fact]
    public void The_message_names_the_restore_command_and_the_projects()
    {
        var message = RestoreCheck.BuildMessage(["/tmp/one", "/tmp/two"]);

        Assert.Contains("dotnet restore", message, StringComparison.Ordinal);
        Assert.Contains("/tmp/one", message, StringComparison.Ordinal);
        Assert.Contains("/tmp/two", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_message_caps_the_listed_projects_but_reports_the_total()
    {
        var directories = Enumerable
            .Range(1, 8)
            .Select(index =>
                $"/tmp/project-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            )
            .ToArray();

        var message = RestoreCheck.BuildMessage(directories);

        Assert.Contains("/tmp/project-1", message, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp/project-8", message, StringComparison.Ordinal);
        Assert.Contains("8", message, StringComparison.Ordinal);
    }
}
