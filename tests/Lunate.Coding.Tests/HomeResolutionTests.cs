namespace Lunate.Coding.Tests;

/// <summary>
/// The one-state-root rule stays one rule: a source scan (the shape <c>LayeringChecker</c> uses)
/// fails on a second place that resolves the user profile, and the variable itself is proven to come
/// from the process environment only. A repository-supplied <c>LUNATE_HOME</c> would choose the
/// directory its own trust records live in, which is the one input the trust store cannot accept.
/// </summary>
public sealed class HomeResolutionTests
{
    [Fact]
    public void Only_the_state_root_resolves_the_user_profile()
    {
        var violations = HomeResolutionChecker.FindViolations(FindRepositoryRoot());

        Assert.True(
            violations.Count == 0,
            "Profile resolution outside LunateHome:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations)
        );
    }

    /// <summary>An allow-list entry without a reason is a bug, not documentation debt.</summary>
    [Fact]
    public void Every_allowed_site_carries_a_reason()
    {
        foreach (HomeResolutionChecker.AllowedSite site in HomeResolutionChecker.AllowedSites)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(site.Reason),
                $"{site.File} ({site.Resolution}) is allow-listed without a reason"
            );
            Assert.True(
                File.Exists(Path.Combine(FindRepositoryRoot(), site.File)),
                $"{site.File} is allow-listed but does not exist"
            );
        }
    }

    /// <summary>
    /// The root is read from the process environment and nowhere else: no <c>.env</c> file, settings
    /// file, <c>AGENTS.md</c> or project file can supply it. Today nothing loads <c>.env</c>; this
    /// test is what keeps a future dependency from making the claim false.
    /// </summary>
    [Fact]
    public void The_root_variable_is_read_from_the_process_environment_only()
    {
        string repositoryRoot = FindRepositoryRoot();
        string stateRoot = Path.Combine(repositoryRoot, HomeResolutionChecker.StateRootFile);

        var readers = new List<string>();
        foreach (string file in HomeResolutionChecker.SourceFiles(repositoryRoot))
        {
            string text = File.ReadAllText(file);
            if (
                text.Contains("GetEnvironmentVariable(\"LUNATE_HOME\")", StringComparison.Ordinal)
                || text.Contains("GetEnvironmentVariable(Variable)", StringComparison.Ordinal)
            )
            {
                // The constant is written with forward slashes; Windows produces backslashes.
                readers.Add(
                    Path.GetRelativePath(repositoryRoot, file)
                        .Replace(Path.DirectorySeparatorChar, '/')
                );
            }
        }

        Assert.Equal([HomeResolutionChecker.StateRootFile], readers);
        Assert.True(File.Exists(stateRoot));

        // Nothing in production may set it either: a settings or project loader that wrote the
        // variable would make the environment a suggestion rather than the one source.
        Assert.DoesNotContain(
            HomeResolutionChecker.SourceFiles(repositoryRoot),
            file =>
                File.ReadAllText(file)
                    .Contains("SetEnvironmentVariable(\"LUNATE_HOME\"", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void No_production_file_loads_a_dotenv_file()
    {
        string repositoryRoot = FindRepositoryRoot();

        Assert.DoesNotContain(
            HomeResolutionChecker.SourceFiles(repositoryRoot),
            file =>
            {
                string text = File.ReadAllText(file);
                return text.Contains("\".env\"", StringComparison.Ordinal)
                    || text.Contains("DotNetEnv", StringComparison.Ordinal);
            }
        );

        // A package reference is the other way a `.env` loader could arrive.
        Assert.DoesNotContain(
            Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src"),
                "*.csproj",
                SearchOption.AllDirectories
            ),
            file => File.ReadAllText(file).Contains("DotNetEnv", StringComparison.Ordinal)
        );
    }

    private static string FindRepositoryRoot()
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
