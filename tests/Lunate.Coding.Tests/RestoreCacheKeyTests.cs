using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>
/// The address of the fixture restore cache. It follows the files a restore reads and never the
/// repository metadata: git rewrites <c>.git</c> while a test runs — maintenance, index and ref
/// locks — and a key that read it both disagreed between identical trees and could stat a file
/// that git had just deleted, which failed the test instead of missing the cache.
/// </summary>
public sealed class RestoreCacheKeyTests
{
    [Fact]
    public void Git_metadata_never_changes_the_key()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Fixture.sln"), "solution");
        string objects = Path.Combine(temp.Root, ".git", "objects");
        Directory.CreateDirectory(objects);
        File.WriteAllText(Path.Combine(objects, "maintenance.lock"), "git");

        string before = RestoreCache.Key(temp.Root, "Fixture.sln");

        File.Delete(Path.Combine(objects, "maintenance.lock"));
        File.WriteAllText(Path.Combine(temp.Root, ".git", "index"), "different");

        Assert.Equal(before, RestoreCache.Key(temp.Root, "Fixture.sln"));
    }

    [Fact]
    public void A_changed_source_file_changes_the_key()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Fixture.sln"), "solution");
        File.WriteAllText(temp.File("Program.cs"), "class A { }");

        string before = RestoreCache.Key(temp.Root, "Fixture.sln");
        File.WriteAllText(temp.File("Program.cs"), "class B { }");

        Assert.NotEqual(before, RestoreCache.Key(temp.Root, "Fixture.sln"));
    }

    [Fact]
    public void The_solution_name_is_part_of_the_key()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Fixture.sln"), "solution");

        Assert.NotEqual(
            RestoreCache.Key(temp.Root, "Fixture.sln"),
            RestoreCache.Key(temp.Root, "Other.sln")
        );
    }

    [Fact]
    public void Stored_artifacts_never_carry_git()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Fixture.slnx"), "solution");
        string obj = Path.Combine(temp.Root, "obj");
        Directory.CreateDirectory(obj);
        File.WriteAllText(Path.Combine(obj, "project.assets.json"), "{}");
        // Poison a snapshot must never travel: a foreign repository metadata would be copied
        // over the next test's fresh .git and mix refs from one snapshot with its objects.
        string objects = Path.Combine(temp.Root, ".git", "objects", "aa");
        Directory.CreateDirectory(objects);
        File.WriteAllText(Path.Combine(objects, "deadbeef"), "poison");

        string key = RestoreCache.Key(temp.Root, "Fixture.slnx");
        RestoreCache.Store(key, temp.Root);

        using var destination = new TempDirectory();
        string refs = Path.Combine(destination.Root, ".git", "refs", "heads");
        Directory.CreateDirectory(refs);
        File.WriteAllText(Path.Combine(refs, "main"), "0123456789abcde0");

        Assert.True(RestoreCache.TryReuse(key, destination.Root));
        Assert.True(
            File.Exists(Path.Combine(destination.Root, "obj", "project.assets.json")),
            "restore output should arrive"
        );
        Assert.False(
            Directory.Exists(Path.Combine(destination.Root, ".git", "objects")),
            ".git must never travel"
        );
        Assert.Equal("0123456789abcde0", File.ReadAllText(Path.Combine(refs, "main")));
    }

    [Fact]
    public void Restore_output_is_ignored()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Fixture.sln"), "solution");

        string before = RestoreCache.Key(temp.Root, "Fixture.sln");

        string obj = Path.Combine(temp.Root, "obj");
        Directory.CreateDirectory(obj);
        File.WriteAllText(Path.Combine(obj, "project.assets.json"), "{}");

        Assert.Equal(before, RestoreCache.Key(temp.Root, "Fixture.sln"));
    }
}
