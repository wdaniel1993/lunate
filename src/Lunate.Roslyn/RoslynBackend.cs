using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace Lunate.Roslyn;

/// <summary>
/// The in-process Roslyn backend (ADR-0006, ADR-0014): MSBuild-located on first use, solution
/// scoped, restore-checked before compiling and soft-failing — every failure path returns a
/// result, never a raw exception to the caller.
/// </summary>
public sealed class RoslynBackend : ICSharpBackend, IDisposable
{
    private const int MaxWorkspaces = 2;
    private const int MaxFailures = 20;

    private readonly string _root;
    private readonly Func<BootstrapResult> _bootstrap;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LruCache<WorkspaceEntry> _workspaces = new(MaxWorkspaces);
    private readonly HashSet<string> _dirty = new(PathIdentity.Comparer);

    private string? _loadedSolution;
    private WorkspaceLoadResult? _loadedResult;

    /// <summary>Creates a backend for the run/worktree root; nothing loads until <see cref="LoadAsync"/>.</summary>
    public RoslynBackend(string worktreeRoot)
        : this(worktreeRoot, MsBuildBootstrap.EnsureInitialized) { }

    internal RoslynBackend(string worktreeRoot, Func<BootstrapResult> bootstrap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);
        _root = PathIdentity.Canonicalize(worktreeRoot);
        _bootstrap = bootstrap;
    }

    public async ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(
                WorkspaceStatus.Partial,
                $"the solution could not be loaded: {exception.Message}"
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
        DiagnosticsScope scope,
        CancellationToken ct
    )
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await GetDiagnosticsCoreAsync(scope, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new DiagnosticsResult(
                [],
                0,
                0,
                false,
                $"diagnostics failed: {exception.Message}"
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<SymbolSearchResult> FindSymbolAsync(string name, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await FindSymbolCoreAsync(name, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new SymbolSearchResult(
                SymbolSearchStatus.Partial,
                $"symbol search failed: {exception.Message}",
                [],
                0
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public void NotifyFileChanged(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return;
        }

        string canonical;
        try
        {
            canonical = PathIdentity.Canonicalize(absolutePath);
        }
        catch (Exception exception)
            when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return;
        }

        _gate.Wait();
        try
        {
            _dirty.Add(canonical);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            _workspaces.Dispose();
            _loadedSolution = null;
            _loadedResult = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WorkspaceLoadResult> LoadCoreAsync(CancellationToken ct)
    {
        if (
            _loadedSolution is { } cachedPath
            && _loadedResult is { } cachedResult
            && _workspaces.TryGet(cachedPath, out _)
        )
        {
            return cachedResult;
        }

        var bootstrap = _bootstrap();
        if (!bootstrap.Succeeded)
        {
            return Failure(
                WorkspaceStatus.NoSdk,
                bootstrap.FailureMessage ?? "no .NET SDK could be located"
            );
        }

        var discovery = SolutionDiscovery.Discover(_root);
        if (discovery.SolutionPath is null)
        {
            return Failure(WorkspaceStatus.NoSolution, discovery.Message);
        }

        var solutionPath = PathIdentity.Canonicalize(discovery.SolutionPath);
        if (
            string.Equals(_loadedSolution, solutionPath, PathIdentity.Comparison)
            && _loadedResult is { } alreadyLoaded
            && _workspaces.TryGet(solutionPath, out _)
        )
        {
            return alreadyLoaded;
        }

        var failures = new CappedList<WorkspaceFailure>(MaxFailures);
        var workspace = MSBuildWorkspace.Create();
        try
        {
            workspace.RegisterWorkspaceFailedHandler(args =>
                failures.Add(new WorkspaceFailure(null, args.Diagnostic.Message))
            );
            var solution = await workspace
                .OpenSolutionAsync(solutionPath, cancellationToken: ct)
                .ConfigureAwait(false);
            return CompleteLoad(workspace, solution, solutionPath, failures);
        }
        catch (OperationCanceledException)
        {
            workspace.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            workspace.Dispose();
            var explanation = SdkMismatch.Explain(exception.Message);
            return explanation is not null
                ? Failure(WorkspaceStatus.NoSdk, explanation)
                : Failure(
                    WorkspaceStatus.Partial,
                    $"the solution could not be loaded: {exception.Message}"
                );
        }
    }

    private WorkspaceLoadResult CompleteLoad(
        MSBuildWorkspace workspace,
        Solution solution,
        string solutionPath,
        CappedList<WorkspaceFailure> failures
    )
    {
        var projectPaths = solution
            .Projects.Select(project => project.FilePath)
            .OfType<string>()
            .ToList();
        var missing = RestoreCheck.FindMissing(projectPaths);
        if (missing.Count > 0)
        {
            workspace.Dispose();
            return new WorkspaceLoadResult(
                WorkspaceStatus.RestoreRequired,
                RestoreCheck.BuildMessage(missing),
                solutionPath,
                projectPaths.Count,
                0,
                []
            );
        }

        var documentPaths = solution
            .Projects.SelectMany(project => project.Documents)
            .Select(document => document.FilePath)
            .OfType<string>()
            .Select(PathIdentity.Canonicalize)
            .Distinct(PathIdentity.Comparer)
            .ToList();

        var projectCount = solution.ProjectIds.Count;
        var status = failures.Total > 0 ? WorkspaceStatus.Partial : WorkspaceStatus.Loaded;
        var relativeSolution = PathIdentity.RelativeOrAbsolute(_root, solutionPath);
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"loaded {projectCount} projects, {documentPaths.Count} documents from {relativeSolution}"
        );
        if (failures.Total > 0)
        {
            message += string.Create(
                CultureInfo.InvariantCulture,
                $"; {failures.Total} workspace failures (first: {failures.Items[0].Message})"
            );
        }

        var result = new WorkspaceLoadResult(
            status,
            message,
            solutionPath,
            projectCount,
            documentPaths.Count,
            failures.Items
        )
        {
            TotalFailureCount = failures.Total,
        };

        var entry = new WorkspaceEntry(workspace, documentPaths);
        foreach (var path in documentPaths)
        {
            entry.Freshness.Track(path);
        }

        _workspaces.Set(solutionPath, entry);
        _loadedSolution = solutionPath;
        _loadedResult = result;
        return result;
    }

    private Task<SymbolSearchResult> FindSymbolCoreAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(
                new SymbolSearchResult(
                    SymbolSearchStatus.Loaded,
                    "provide a symbol name to search for",
                    [],
                    0
                )
            );
        }

        if (
            _loadedSolution is not { } solutionPath
            || !_workspaces.TryGet(solutionPath, out var entry)
        )
        {
            return Task.FromResult(
                new SymbolSearchResult(
                    SymbolSearchStatus.NoSolution,
                    "no solution is loaded",
                    [],
                    0
                )
            );
        }

        return SearchAsync(name.Trim(), entry, ct);
    }

    private async Task<SymbolSearchResult> SearchAsync(
        string query,
        WorkspaceEntry entry,
        CancellationToken ct
    )
    {
        var failures = new CappedList<WorkspaceFailure>(MaxFailures);
        var (solution, _) = SyncChangedDocuments(entry, failures);

        var compilations = new List<Compilation>();
        foreach (var project in solution.Projects)
        {
            if (await project.GetCompilationAsync(ct).ConfigureAwait(false) is { } compilation)
            {
                compilations.Add(compilation);
            }
        }

        var collected = SymbolCollector.Collect(compilations, query, _root, ct);
        var status =
            failures.Total > 0 || _loadedResult is { Status: WorkspaceStatus.Partial }
                ? SymbolSearchStatus.Partial
                : SymbolSearchStatus.Loaded;

        return new SymbolSearchResult(
            status,
            DescribeSearch(query, collected),
            collected.Matches,
            collected.TotalMatchCount
        )
        {
            Truncated = collected.Truncated,
            Failures = failures.Items,
            TotalFailureCount = failures.Total,
        };
    }

    private static string DescribeSearch(string query, CollectedSymbols collected)
    {
        if (collected.TotalMatchCount == 0)
        {
            return collected.CaseInsensitiveCandidate is { } candidate
                ? $"no definition found for '{query}'; C# is case-sensitive — did you mean '{candidate}'?"
                : $"no definition found for '{query}'; check the spelling or search for the simple name";
        }

        var noun = collected.TotalMatchCount == 1 ? "definition" : "definitions";
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"found {collected.TotalMatchCount} {noun} for '{query}'"
        );
        if (collected.MetadataMatchCount > 0)
        {
            message += string.Create(
                CultureInfo.InvariantCulture,
                $"; {collected.MetadataMatchCount} metadata-only (no source file)"
            );
        }

        if (collected.Truncated)
        {
            message += string.Create(
                CultureInfo.InvariantCulture,
                $"; showing the first {SymbolCollector.MaxMatches}"
            );
        }

        return message;
    }

    private async Task<DiagnosticsResult> GetDiagnosticsCoreAsync(
        DiagnosticsScope scope,
        CancellationToken ct
    )
    {
        if (
            _loadedSolution is not { } solutionPath
            || !_workspaces.TryGet(solutionPath, out var entry)
        )
        {
            return new DiagnosticsResult([], 0, 0, false, "no solution is loaded");
        }

        var failures = new CappedList<WorkspaceFailure>(MaxFailures);
        var (solution, changed) = SyncChangedDocuments(entry, failures);

        if (scope == DiagnosticsScope.ChangedFiles && changed.Count == 0)
        {
            return new DiagnosticsResult([], 0, 0, false, "no files changed since the last check");
        }

        IReadOnlySet<string>? onlyFiles =
            scope == DiagnosticsScope.ChangedFiles
                ? changed.ToHashSet(PathIdentity.Comparer)
                : null;
        var projects =
            scope == DiagnosticsScope.ChangedFiles
                ? ProjectsForFiles(solution, changed)
                : solution.Projects;

        var collected = await DiagnosticsCollector
            .CollectAsync(projects, onlyFiles, _root, ct)
            .ConfigureAwait(false);

        var scopeDescription =
            scope == DiagnosticsScope.ChangedFiles
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{changed.Count} file(s) changed since the last check"
                )
                : "the whole solution";

        return new DiagnosticsResult(
            collected.Items.Items,
            collected.ErrorCount,
            collected.WarningCount,
            collected.Items.Truncated,
            scopeDescription
        )
        {
            Failures = failures.Items,
            TotalFailureCount = failures.Total,
        };
    }

    /// <summary>
    /// Re-reads files changed since the last sync (content stamps plus explicit dirty marks) into
    /// the solution snapshot; failures are collected without discarding the workspace.
    /// </summary>
    private (Solution Solution, IReadOnlyList<string> Changed) SyncChangedDocuments(
        WorkspaceEntry entry,
        CappedList<WorkspaceFailure> failures
    )
    {
        var solution = entry.Workspace.CurrentSolution;
        var changed = entry.Freshness.TakeChanged(entry.DocumentPaths).ToList();
        foreach (var path in entry.DocumentPaths)
        {
            if (_dirty.Remove(path) && !changed.Contains(path, PathIdentity.Comparer))
            {
                changed.Add(path);
            }
        }

        if (changed.Count > 0)
        {
            solution = RefreshDocuments(entry, solution, changed, failures);
        }

        return (solution, changed);
    }

    /// <summary>
    /// Re-reads changed files into the solution snapshot and applies them via
    /// <c>TryApplyChanges</c>; when the apply fails the forked snapshot is still compiled, so
    /// diagnostics see the new text either way (design fallback).
    /// </summary>
    private Solution RefreshDocuments(
        WorkspaceEntry entry,
        Solution solution,
        IReadOnlyList<string> changed,
        CappedList<WorkspaceFailure> failures
    )
    {
        foreach (var path in changed)
        {
            var display = PathIdentity.RelativeOrAbsolute(_root, path);
            if (!File.Exists(path))
            {
                failures.Add(
                    new WorkspaceFailure(
                        null,
                        $"deleted file {display}; diagnostics for it are skipped until it is restored"
                    )
                );
                continue;
            }

            var document = FindDocument(solution, path);
            if (document is null)
            {
                failures.Add(
                    new WorkspaceFailure(null, $"{display} is not part of the loaded solution")
                );
                continue;
            }

            try
            {
                var text = SourceText.From(File.ReadAllText(path));
                solution = solution.WithDocumentText(
                    document.Id,
                    text,
                    PreservationMode.PreserveIdentity
                );
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(
                    new WorkspaceFailure(null, $"could not re-read {display}: {exception.Message}")
                );
            }
        }

        try
        {
            entry.Workspace.TryApplyChanges(solution);
        }
        catch (Exception exception)
        {
            failures.Add(
                new WorkspaceFailure(null, $"could not apply changed files: {exception.Message}")
            );
        }

        return solution;
    }

    private static IEnumerable<Project> ProjectsForFiles(
        Solution solution,
        IReadOnlyList<string> canonicalPaths
    )
    {
        var projectIds = new HashSet<ProjectId>();
        foreach (var path in canonicalPaths)
        {
            if (FindDocument(solution, path) is { } document)
            {
                projectIds.Add(document.Project.Id);
            }
        }

        return solution.Projects.Where(project => projectIds.Contains(project.Id));
    }

    private static Document? FindDocument(Solution solution, string canonicalPath)
    {
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (
                    document.FilePath is { } path
                    && PathIdentity.Comparer.Equals(PathIdentity.Canonicalize(path), canonicalPath)
                )
                {
                    return document;
                }
            }
        }

        return null;
    }

    private static WorkspaceLoadResult Failure(WorkspaceStatus status, string message) =>
        new(status, message, null, 0, 0, []);
}
