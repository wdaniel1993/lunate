using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

internal static class TestPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find lunate.sln above {AppContext.BaseDirectory}."
        );
    }
}

internal sealed class ThrowingChatClient : IChatClient
{
    public bool Called { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        Called = true;
        throw new InvalidOperationException("The provider must not be called during replay.");
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        Called = true;
        throw new InvalidOperationException("The provider must not be called during replay.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

internal sealed class EnvironmentScope : IDisposable
{
    private readonly (string Name, string? Value)[] _previous;

    public EnvironmentScope(params (string Name, string? Value)[] variables)
    {
        _previous =
        [
            .. variables.Select(variable =>
                (variable.Name, Environment.GetEnvironmentVariable(variable.Name))
            ),
        ];
        foreach ((string name, string? value) in variables)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach ((string name, string? value) in _previous)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "lunate-agent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
