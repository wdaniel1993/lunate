using System.Globalization;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>Records every extension log line in emission order for assertions.</summary>
internal sealed class RecordingExtensionLog : IExtensionLog
{
    private readonly object _gate = new();
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }

    public void Info(string message) => Add(message);

    public void Warn(string message) => Add(message);

    public void Error(string message) => Add(message);

    public bool Contains(string text) =>
        Messages.Any(message => message.Contains(text, StringComparison.Ordinal));

    private void Add(string message)
    {
        lock (_gate)
        {
            _messages.Add(message);
        }
    }
}

/// <summary>A trust prompt with one scripted decision per expected call; exhaustion throws.</summary>
internal sealed class ScriptedTrustPrompt(params bool[] decisions) : IExtensionTrustPrompt
{
    private readonly Queue<bool> _decisions = new(decisions);

    public int Calls { get; private set; }

    public ValueTask<bool> ApproveAsync(
        ExtensionTrustRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        Calls++;
        if (!_decisions.TryDequeue(out bool decision))
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"ScriptedTrustPrompt has no decision left for extension '{request.Descriptor.Id}' (call {Calls}); queue one decision per expected trust prompt."
                )
            );
        }

        return ValueTask.FromResult(decision);
    }
}

/// <summary>The HelloExtension fixture copied into this test project's output.</summary>
internal static class HelloFixture
{
    internal static string SourceDirectory =>
        Path.Combine(AppContext.BaseDirectory, "TestExtensions", "HelloExtension");

    internal static string Install(string targetDirectory, string id = "hello")
    {
        Directory.CreateDirectory(targetDirectory);
        CopyDirectory(SourceDirectory, targetDirectory);
        File.WriteAllText(
            Path.Combine(targetDirectory, "extension.json"),
            $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"^1.5.0","entryAssembly":"HelloExtension.dll"}"""
        );
        return targetDirectory;
    }

    internal static void WriteSettings(string storePath, string id, string markerPath)
    {
        string directory = Path.Combine(storePath, "extensions-settings");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, id + ".json"),
            System.Text.Json.JsonSerializer.Serialize(new { markerPath })
        );
    }

    internal static void CopyDirectory(string source, string destination)
    {
        foreach (
            string directory in Directory.EnumerateDirectories(
                source,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            Directory.CreateDirectory(
                Path.Combine(destination, Path.GetRelativePath(source, directory))
            );
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}

/// <summary>The seams every session-level test uses so the real store is never read.</summary>
internal static class TestExtensionSeams
{
    internal static ExtensionSeams Empty(TempDirectory temp) =>
        new() { StorePath = temp.File("extensions-store"), Log = new RecordingExtensionLog() };
}
