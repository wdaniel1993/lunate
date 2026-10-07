namespace Lunate.Agent.Tests;

public sealed class SessionPathsTests
{
    [Fact]
    public void ForProject_is_stable_for_the_same_directory_and_differs_for_others()
    {
        string project = Path.Combine(Path.GetTempPath(), "lunate-paths-project");

        Assert.Equal(SessionPaths.ForProject(project), SessionPaths.ForProject(project));
        Assert.NotEqual(
            SessionPaths.ForProject(project),
            SessionPaths.ForProject(Path.Combine(Path.GetTempPath(), "lunate-paths-other"))
        );
    }

    [Fact]
    public void ForProject_normalizes_the_working_directory()
    {
        string project = Path.Combine(Path.GetTempPath(), "lunate-paths-normalized");

        Assert.Equal(
            SessionPaths.ForProject(project),
            SessionPaths.ForProject(project + Path.DirectorySeparatorChar + ".")
        );
    }

    [Fact]
    public void ForProject_lives_under_the_user_profile_sessions_directory()
    {
        string path = SessionPaths.ForProject(TestPaths.RepositoryRoot);

        string expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lunate",
            "sessions"
        );
        Assert.Equal(expectedRoot, Path.GetDirectoryName(path));
        Assert.Matches("^[0-9a-f]{8}$", Path.GetFileName(path));
    }

    [Fact]
    public void SessionFileName_appends_the_jsonl_extension()
    {
        Assert.Equal(
            "s_20261006-120000-abcd.jsonl",
            SessionPaths.SessionFileName("s_20261006-120000-abcd")
        );
    }

    [Theory]
    [InlineData("s_1/../evil")]
    [InlineData("a\\b")]
    [InlineData("..")]
    public void SessionFileName_rejects_path_separators_and_dot_dot(string sessionId)
    {
        Assert.Throws<ArgumentException>(() => SessionPaths.SessionFileName(sessionId));
    }
}
