using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class PathIdentityTests
{
    [Fact]
    public void A_path_under_the_root_is_relative_with_forward_slashes()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Root, "src", "App", "Program.cs");

        var display = PathIdentity.RelativeOrAbsolute(temp.Root, path);

        Assert.Equal("src/App/Program.cs", display);
    }

    [Fact]
    public void A_path_outside_the_root_stays_absolute()
    {
        using var temp = new TempDirectory();
        var outside = Path.Combine(Path.GetTempPath(), "somewhere", "else.cs");

        var display = PathIdentity.RelativeOrAbsolute(temp.Root, outside);

        Assert.Equal(outside, display);
    }

    [Fact]
    public void The_root_itself_uses_a_dot()
    {
        using var temp = new TempDirectory();

        var display = PathIdentity.RelativeOrAbsolute(temp.Root, temp.Root);

        Assert.Equal(".", display);
    }

    [Fact]
    public void A_symlinked_path_resolves_to_its_target()
    {
        using var temp = new TempDirectory();
        var target = temp.Subdirectory("real");
        var link = temp.File("link");
        System.IO.Directory.CreateSymbolicLink(link, target);

        var canonical = PathIdentity.Canonicalize(link);

        Assert.Equal(PathIdentity.Canonicalize(target), canonical);
    }

    [Fact]
    public void Path_comparison_follows_the_platform_case_rules()
    {
        var expected = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        Assert.Equal(expected, PathIdentity.Comparison);
    }
}
