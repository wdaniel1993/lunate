namespace Lunate.Coding.Tests;

/// <summary>
/// An in-memory workspace seam: deterministic entries and contents, a call counter for the
/// laziness assertions, and an optional gate that blocks <see cref="Enumerate"/> so the
/// indexing-notice path is testable.
/// </summary>
internal sealed class FakeWorkspaceFiles : IWorkspaceFiles
{
    private readonly List<WorkspaceEntry> _entries = [];
    private readonly Dictionary<string, string> _contents = new(StringComparer.Ordinal);

    public int EnumerateCalls { get; private set; }

    /// <summary>When set, <see cref="Enumerate"/> blocks until the test completes it.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public FakeWorkspaceFiles AddDirectory(string path)
    {
        _entries.Add(new WorkspaceEntry(path, IsDirectory: true));
        return this;
    }

    public FakeWorkspaceFiles AddFile(string path, string content = "")
    {
        _entries.Add(new WorkspaceEntry(path, IsDirectory: false));
        _contents[path] = content;
        return this;
    }

    public IReadOnlyList<WorkspaceEntry> Enumerate()
    {
        EnumerateCalls++;
        Gate?.Task.GetAwaiter().GetResult();
        return _entries;
    }

    public string? ReadAllText(string relativePath) =>
        _contents.TryGetValue(relativePath, out string? content) ? content : null;
}
