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

    /// <summary>A catalog declaring the given reasoning style (null declares none) for each model id.</summary>
    internal static ModelCatalog WithReasoning(string? style, params string[] modelIds)
    {
        string[] ids = modelIds.Length == 0 ? [ModelId] : modelIds;
        string reasoning = style is null ? "" : string.Concat(",\"reasoning\":\"", style, "\"");
        string models = string.Join(
            ",",
            ids.Select(modelId =>
                $$"""{"id":"{{modelId}}","provider":"openai","contextWindow":1000,"supportsTools":true{{reasoning}}}"""
            )
        );
        string path = Path.Combine(Path.GetTempPath(), $"lunate-models-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $$"""{"schemaVersion":1,"models":[{{models}}]}""");
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

internal static class SessionImageTestData
{
    internal const string PngDigest =
        "7f47b756761a46e6d4a4d96f0d8a4448f8449235009d1f3ad1493f5c773c19e8";

    internal static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    internal static readonly byte[] PngBytes =
    [
        0x89,
        0x50,
        0x4E,
        0x47,
        0x0D,
        0x0A,
        0x1A,
        0x0A,
        1,
        2,
        3,
    ];

    internal static Session CreateSession(TempDirectory temp) =>
        Session.Create(temp.File("s.jsonl"), "/work", new FixedTimeProvider(Start));
}
