using Lunate.Roslyn;

namespace Lunate.Coding.Tests;

/// <summary>A configurable backend double; <see cref="Hold"/> makes a call in-flight until set.</summary>
internal sealed class FakeWorkspaceBackend(string root) : ICSharpBackend, IDisposable
{
    public string Root { get; } = root;

    public TaskCompletionSource? Hold { get; init; }

    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Disposed { get; private set; }

    public int DisposeCalls { get; private set; }

    public int LoadCalls { get; private set; }

    public ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct)
    {
        LoadCalls++;
        return ValueTask.FromResult(
            new WorkspaceLoadResult(WorkspaceStatus.Loaded, "loaded", Root, 1, 1, [])
        );
    }

    public async ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
        DiagnosticsScope scope,
        CancellationToken ct
    )
    {
        Started.TrySetResult();
        if (Hold is not null)
        {
            await Hold.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        return new DiagnosticsResult([], 0, 0, false, "the whole solution");
    }

    public ValueTask<SymbolSearchResult> FindSymbolAsync(string name, CancellationToken ct) =>
        ValueTask.FromResult(new SymbolSearchResult(SymbolSearchStatus.Loaded, "found", [], 0));

    public ValueTask<ReferencesResult> FindReferencesAsync(string name, CancellationToken ct) =>
        ValueTask.FromResult(new ReferencesResult(SymbolSearchStatus.Loaded, "found", null, [], 0));

    public ValueTask<OutlineResult> OutlineAsync(string file, CancellationToken ct) =>
        ValueTask.FromResult(new OutlineResult("outlined", [], 0));

    public ValueTask<RenamePlanResult> PlanRenameAsync(
        string name,
        string newName,
        CancellationToken ct
    ) => ValueTask.FromResult(new RenamePlanResult(SymbolSearchStatus.Loaded, "planned", [], 0, 0));

    public void NotifyFileChanged(string absolutePath) { }

    public void Dispose()
    {
        Disposed = true;
        DisposeCalls++;
    }
}
