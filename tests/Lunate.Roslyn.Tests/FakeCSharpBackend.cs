using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

/// <summary>A configurable backend double for tool tests: records calls, throws on demand.</summary>
internal sealed class FakeCSharpBackend : ICSharpBackend
{
    public WorkspaceLoadResult LoadResult { get; set; } =
        new(WorkspaceStatus.Loaded, "loaded 1 projects", "/tmp/App.slnx", 1, 1, []);

    public ReferencesResult References { get; set; } =
        new(SymbolSearchStatus.Loaded, "found 0 references", null, [], 0);

    public OutlineResult Outline { get; set; } = new("found 0 declarations", [], 0);

    public RenamePlanResult RenamePlan { get; set; } =
        new(
            SymbolSearchStatus.Loaded,
            "planned 0 change(s) in 0 file(s); nothing was changed — apply via edit/write",
            [],
            0,
            0
        );

    public bool ReferencesThrow { get; set; }

    public bool OutlineThrow { get; set; }

    public bool RenameThrow { get; set; }

    public int LoadCalls { get; private set; }

    public int ReferencesCalls { get; private set; }

    public int OutlineCalls { get; private set; }

    public int RenameCalls { get; private set; }

    public string? LastName { get; private set; }

    public string? LastNewName { get; private set; }

    public string? LastFile { get; private set; }

    public ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct)
    {
        LoadCalls++;
        return ValueTask.FromResult(LoadResult);
    }

    public ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
        DiagnosticsScope scope,
        CancellationToken ct
    ) => throw new InvalidOperationException("diagnostics are not used by these tools");

    public ValueTask<SymbolSearchResult> FindSymbolAsync(string name, CancellationToken ct) =>
        throw new InvalidOperationException("symbol search is not used by these tools");

    public ValueTask<ReferencesResult> FindReferencesAsync(string name, CancellationToken ct)
    {
        ReferencesCalls++;
        LastName = name;
        if (ReferencesThrow)
        {
            throw new InvalidOperationException("boom");
        }

        return ValueTask.FromResult(References);
    }

    public ValueTask<OutlineResult> OutlineAsync(string file, CancellationToken ct)
    {
        OutlineCalls++;
        LastFile = file;
        if (OutlineThrow)
        {
            throw new InvalidOperationException("boom");
        }

        return ValueTask.FromResult(Outline);
    }

    public ValueTask<RenamePlanResult> PlanRenameAsync(
        string name,
        string newName,
        CancellationToken ct
    )
    {
        RenameCalls++;
        LastName = name;
        LastNewName = newName;
        if (RenameThrow)
        {
            throw new InvalidOperationException("boom");
        }

        return ValueTask.FromResult(RenamePlan);
    }

    public void NotifyFileChanged(string absolutePath) { }
}
