using System.Diagnostics;
using System.Globalization;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

internal static class TestCatalog
{
    internal const string ModelId = "compaction-test";

    internal static ModelCatalog WithWindow(int window, string modelId = ModelId)
    {
        string path = Path.Combine(Path.GetTempPath(), $"lunate-models-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            path,
            $$"""
            {"schemaVersion":1,"models":[{"id":"{{modelId}}","provider":"openai","contextWindow":{{window.ToString(
                CultureInfo.InvariantCulture
            )}},"supportsTools":true}]}
            """
        );
        try
        {
            return ModelCatalog.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

internal sealed class TraceCapture : IDisposable
{
    private readonly TextWriterTraceListener _listener;
    private readonly StringWriter _writer = new();

    public TraceCapture()
    {
        _listener = new TextWriterTraceListener(_writer);
        Trace.Listeners.Add(_listener);
    }

    public string Text => _writer.ToString();

    public void Dispose()
    {
        Trace.Listeners.Remove(_listener);
        _listener.Dispose();
        _writer.Dispose();
    }
}

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
