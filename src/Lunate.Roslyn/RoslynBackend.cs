using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using SymbolFinder = Microsoft.CodeAnalysis.FindSymbols.SymbolFinder;

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
    private readonly ConcurrentDictionary<string, byte> _dirty = new(PathIdentity.Comparer);

    /// <summary>
    /// Test seam: invoked with each path the refresh just read and stamped, before the apply -
    /// lets tests inject a deterministic write that races the refresh.
    /// </summary>
    private readonly Action<string>? _onRefreshRead;

    private string? _loadedSolution;
    private WorkspaceLoadResult? _loadedResult;
    private WorkspaceLoadResult? _lastLoad;
    private int _reloadRequested;
    private int _loadCount;

    /// <summary>Creates a backend for the run/worktree root; nothing loads until <see cref="LoadAsync"/>.</summary>
    public RoslynBackend(string worktreeRoot)
        : this(worktreeRoot, MsBuildBootstrap.EnsureInitialized) { }

    internal RoslynBackend(
        string worktreeRoot,
        Func<BootstrapResult> bootstrap,
        Action<string>? onRefreshRead = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);
        _root = PathIdentity.Canonicalize(worktreeRoot);
        _bootstrap = bootstrap;
        _onRefreshRead = onRefreshRead;
    }

    public async ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await LoadCoreAsync(ct).ConfigureAwait(false);
            _lastLoad = result;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = Failure(
                WorkspaceStatus.Partial,
                $"the solution could not be loaded: {exception.Message}"
            );
            _lastLoad = failure;
            return failure;
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

    public async ValueTask<ReferencesResult> FindReferencesAsync(string name, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await FindReferencesCoreAsync(name, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ReferencesResult(
                SymbolSearchStatus.Partial,
                $"reference search failed: {exception.Message}",
                null,
                [],
                0
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask<OutlineResult> OutlineAsync(string file, CancellationToken ct)
    {
        _gate.Wait(ct);
        try
        {
            return ValueTask.FromResult(OutlineCore(file));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(
                new OutlineResult($"outline failed: {exception.Message}", [], 0)
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RenamePlanResult> PlanRenameAsync(
        string name,
        string newName,
        CancellationToken ct
    )
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await PlanRenameCoreAsync(name, newName, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new RenamePlanResult(
                SymbolSearchStatus.Partial,
                $"rename planning failed: {exception.Message}",
                [],
                0,
                0
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>How often a solution was actually loaded; observable for tests.</summary>
    internal int LoadCount => Volatile.Read(ref _loadCount);

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

        // Mark-only and lock-free: the file-change bus runs handlers under a timeout, so a mark
        // must never wait for the workspace gate (a cold load or long compile holds it).
        _dirty[canonical] = 0;
    }

    /// <summary>
    /// Marks the workspace for a reload: a changed project, solution or build-configuration file
    /// invalidates the whole compilation, so the next load disposes the loaded solution and reads
    /// disk again. Lock-free like <see cref="NotifyFileChanged"/>.
    /// </summary>
    public void NotifyProjectChanged() => Interlocked.Exchange(ref _reloadRequested, 1);

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
        if (Interlocked.Exchange(ref _reloadRequested, 0) == 1)
        {
            DiscardLoadedSolution();
        }

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
        Interlocked.Increment(ref _loadCount);
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

    /// <summary>
    /// Drops the loaded solution (disposed through the workspace cache), so the next check opens
    /// the solution from disk again.
    /// </summary>
    private void DiscardLoadedSolution()
    {
        if (_loadedSolution is { } loaded)
        {
            _workspaces.Remove(loaded);
        }

        _loadedSolution = null;
        _loadedResult = null;
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
        var compilations = await CompilationsAsync(solution, ct).ConfigureAwait(false);

        var collected = SymbolCollector.Collect(compilations, query, _root, ct);
        var status = SearchStatus(failures);

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

    private async Task<ReferencesResult> FindReferencesCoreAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new ReferencesResult(
                SymbolSearchStatus.Loaded,
                "provide a symbol name to search for",
                null,
                [],
                0
            );
        }

        if (
            _loadedSolution is not { } solutionPath
            || !_workspaces.TryGet(solutionPath, out var entry)
        )
        {
            return new ReferencesResult(
                SymbolSearchStatus.NoSolution,
                "no solution is loaded",
                null,
                [],
                0
            );
        }

        var query = name.Trim();
        var failures = new CappedList<WorkspaceFailure>(MaxFailures);
        var (solution, _) = SyncChangedDocuments(entry, failures);
        var compilations = await CompilationsAsync(solution, ct).ConfigureAwait(false);
        var resolved = SymbolResolver.Resolve(compilations, query, _root, ct);
        var status = SearchStatus(failures);

        if (resolved.Sources.Count == 0)
        {
            if (resolved.MetadataMatches.Count > 0)
            {
                return new ReferencesResult(
                    status,
                    "metadata symbol — no source references",
                    resolved.MetadataMatches[0],
                    [],
                    0
                )
                {
                    Failures = failures.Items,
                    TotalFailureCount = failures.Total,
                };
            }

            return new ReferencesResult(
                status,
                NotFoundMessage(query, resolved.CaseInsensitiveCandidate),
                null,
                [],
                0
            )
            {
                Failures = failures.Items,
                TotalFailureCount = failures.Total,
            };
        }

        if (resolved.Sources.Count > 1)
        {
            return new ReferencesResult(
                status,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{query}' matches {resolved.Sources.Count} symbols; references need a unique symbol — use a dotted path to disambiguate"
                ),
                null,
                [],
                0
            )
            {
                Candidates = Candidates(resolved),
                Failures = failures.Items,
                TotalFailureCount = failures.Total,
            };
        }

        var target = resolved.Sources[0];
        var declaration = target.Declarations[0];
        if (declaration.FromMetadata)
        {
            return new ReferencesResult(
                status,
                "metadata symbol — no source references",
                declaration,
                [],
                0
            )
            {
                Failures = failures.Items,
                TotalFailureCount = failures.Total,
            };
        }

        List<ReferenceLocation> usages = [];
        var found = await SymbolFinder
            .FindReferencesAsync(target.Symbol, solution, ct)
            .ConfigureAwait(false);
        foreach (var referenced in found)
        {
            foreach (var location in referenced.Locations)
            {
                if (
                    location.IsCandidateLocation
                    || location.Location.SourceTree is not { } tree
                    || string.IsNullOrWhiteSpace(tree.FilePath)
                    || IsDeclarationLocation(target.Symbol, tree, location.Location.SourceSpan)
                )
                {
                    continue;
                }

                var position = location.Location.GetLineSpan().StartLinePosition;
                usages.Add(
                    new ReferenceLocation(
                        PathIdentity.RelativeOrAbsolute(_root, tree.FilePath),
                        position.Line + 1,
                        position.Character + 1
                    )
                );
            }
        }

        var bounded = ReferencesCollector.Build(usages);
        var message =
            bounded.Total == 0
                ? string.Create(CultureInfo.InvariantCulture, $"no references found for '{query}'")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"found {bounded.Total} references to '{query}'"
                );

        return new ReferencesResult(status, message, declaration, bounded.Items, bounded.Total)
        {
            Truncated = bounded.Truncated,
            Failures = failures.Items,
            TotalFailureCount = failures.Total,
        };
    }

    private OutlineResult OutlineCore(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return new OutlineResult("provide a file path relative to the worktree root", [], 0);
        }

        string canonical;
        try
        {
            var candidate = Path.IsPathRooted(file) ? file : Path.Combine(_root, file);
            canonical = PathIdentity.Canonicalize(candidate);
        }
        catch (Exception exception)
            when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return new OutlineResult(
                $"the file path '{file}' could not be resolved: {exception.Message}",
                [],
                0
            );
        }

        var display = PathIdentity.RelativeOrAbsolute(_root, canonical);
        if (!PathIdentity.IsUnder(_root, canonical))
        {
            return new OutlineResult(
                $"'{display}' is outside the worktree root; outline files inside it",
                [],
                0
            );
        }

        if (!File.Exists(canonical))
        {
            return new OutlineResult(
                $"file not found: '{display}'; check the path relative to the worktree root",
                [],
                0
            );
        }

        string text;
        try
        {
            text = File.ReadAllText(canonical);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new OutlineResult($"could not read '{display}': {exception.Message}", [], 0);
        }

        var walked = OutlineCollector.Walk(CSharpSyntaxTree.ParseText(text, path: canonical));
        var message = walked.Total switch
        {
            0 when string.IsNullOrWhiteSpace(text) => "the file is empty",
            0 => string.Create(
                CultureInfo.InvariantCulture,
                $"no declarations found in '{display}'"
            ),
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"found {walked.Total} declarations in '{display}'"
            ),
        };

        return new OutlineResult(message, walked.Items, walked.Total)
        {
            Truncated = walked.Truncated,
        };
    }

    private async Task<RenamePlanResult> PlanRenameCoreAsync(
        string name,
        string newName,
        CancellationToken ct
    )
    {
        if (!SyntaxFacts.IsValidIdentifier(newName))
        {
            return NoPlan(
                SymbolSearchStatus.Loaded,
                $"'{newName}' is not a valid C# identifier: it must start with a letter or underscore, contain only letters, digits or underscores, and not be a C# keyword"
            );
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return NoPlan(SymbolSearchStatus.Loaded, "provide a symbol name to rename");
        }

        if (
            _loadedSolution is not { } solutionPath
            || !_workspaces.TryGet(solutionPath, out var entry)
        )
        {
            if (
                _lastLoad is
                { Status: WorkspaceStatus.RestoreRequired or WorkspaceStatus.NoSdk } last
            )
            {
                return new RenamePlanResult(
                    last.Status == WorkspaceStatus.RestoreRequired
                        ? SymbolSearchStatus.RestoreRequired
                        : SymbolSearchStatus.NoSdk,
                    last.Message,
                    [],
                    0,
                    0
                );
            }

            return new RenamePlanResult(
                SymbolSearchStatus.NoSolution,
                "no solution is loaded",
                [],
                0,
                0
            );
        }

        var query = name.Trim();
        var failures = new CappedList<WorkspaceFailure>(MaxFailures);
        var (solution, _) = SyncChangedDocuments(entry, failures);
        var compilations = await CompilationsAsync(solution, ct).ConfigureAwait(false);
        var resolved = SymbolResolver.Resolve(compilations, query, _root, ct);
        var status = SearchStatus(failures);

        if (resolved.Sources.Count == 0)
        {
            return resolved.MetadataMatches.Count > 0
                ? NoPlan(status, "metadata symbol — no source references")
                : NoPlan(status, NotFoundMessage(query, resolved.CaseInsensitiveCandidate));
        }

        if (resolved.Sources.Count > 1)
        {
            return NoPlan(
                status,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{query}' matches {resolved.Sources.Count} symbols; rename needs a unique symbol — use a dotted path to disambiguate"
                )
            ) with
            {
                Candidates = Candidates(resolved),
                Failures = failures.Items,
                TotalFailureCount = failures.Total,
            };
        }

        var target = resolved.Sources[0];
        if (target.Declarations.All(declaration => declaration.FromMetadata))
        {
            return NoPlan(status, "metadata symbol — no source references");
        }

        var renamed = await Renamer
            .RenameSymbolAsync(solution, target.Symbol, new SymbolRenameOptions(), newName, ct)
            .ConfigureAwait(false);

        List<RenameText> documents = [];
        foreach (var document in solution.Projects.SelectMany(project => project.Documents))
        {
            if (
                document.FilePath is not { } path
                || renamed.GetDocument(document.Id) is not { } updated
            )
            {
                continue;
            }

            var oldText = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            var newText = (await updated.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            if (!string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                documents.Add(
                    new RenameText(PathIdentity.RelativeOrAbsolute(_root, path), oldText, newText)
                );
            }
        }

        documents.Sort((left, right) => string.CompareOrdinal(left.File, right.File));
        var plan = RenamePlanner.Build(documents);
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"planned {plan.TotalChangeCount} change(s) in {plan.TotalFileCount} file(s); nothing was changed — apply via edit/write"
        );

        return new RenamePlanResult(
            status,
            message,
            plan.Changes,
            plan.TotalFileCount,
            plan.TotalChangeCount
        )
        {
            Truncated = plan.Truncated,
            Failures = failures.Items,
            TotalFailureCount = failures.Total,
        };
    }

    private static async Task<List<Compilation>> CompilationsAsync(
        Solution solution,
        CancellationToken ct
    )
    {
        List<Compilation> compilations = [];
        foreach (var project in solution.Projects)
        {
            if (await project.GetCompilationAsync(ct).ConfigureAwait(false) is { } compilation)
            {
                compilations.Add(compilation);
            }
        }

        return compilations;
    }

    private static bool IsDeclarationLocation(ISymbol symbol, SyntaxTree tree, TextSpan span)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree == tree && reference.Span.Contains(span))
            {
                return true;
            }
        }

        return false;
    }

    private static string NotFoundMessage(string query, string? caseInsensitiveCandidate) =>
        caseInsensitiveCandidate is { } candidate
            ? $"no definition found for '{query}'; C# is case-sensitive — did you mean '{candidate}'?"
            : $"no definition found for '{query}'; check the spelling or search for the simple name";

    private static IReadOnlyList<SymbolMatch> Candidates(ResolvedSymbols resolved)
    {
        var capped = new CappedList<SymbolMatch>(SymbolCollector.MaxMatches);
        foreach (var match in resolved.OrderedMatches)
        {
            capped.Add(match);
        }

        return capped.Items;
    }

    private static RenamePlanResult NoPlan(SymbolSearchStatus status, string message) =>
        new(
            status,
            string.Concat(message, "; nothing was changed — apply via edit/write"),
            [],
            0,
            0
        );

    private SymbolSearchStatus SearchStatus(CappedList<WorkspaceFailure> failures) =>
        failures.Total > 0 || _loadedResult is { Status: WorkspaceStatus.Partial }
            ? SymbolSearchStatus.Partial
            : SymbolSearchStatus.Loaded;

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
            if (_dirty.TryRemove(path, out _) && !changed.Contains(path, PathIdentity.Comparer))
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
    /// diagnostics see the new text either way (design fallback). A write that races the refresh
    /// is neither applied nor overwritten: a path whose stamp moves across the read (or between
    /// the read and the apply) is left for the next scan, and the freshness baseline advances
    /// only when the disk still holds exactly the applied text.
    /// </summary>
    private Solution RefreshDocuments(
        WorkspaceEntry entry,
        Solution solution,
        IReadOnlyList<string> changed,
        CappedList<WorkspaceFailure> failures
    )
    {
        // Phase 1: read each changed file, stamping both sides of the read so a write landing
        // mid-read is detectable instead of being applied and re-baselined blindly.
        List<RefreshRead> reads = [];
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
                var stamp = DocumentFreshness.StampOf(path);
                var text = File.ReadAllText(path);
                if (stamp is null || stamp != DocumentFreshness.StampOf(path))
                {
                    // A write raced the read; the stamp scan still sees the disk differ from its
                    // baseline, so the path re-flags on the next check without further handling.
                    continue;
                }

                _onRefreshRead?.Invoke(path);
                reads.Add(new RefreshRead(path, document.Id, stamp.Value, text));
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(
                    new WorkspaceFailure(null, $"could not re-read {display}: {exception.Message}")
                );
            }
        }

        // Phase 2: apply only reads whose stamp still matches the disk, so the write-back cannot
        // overwrite a write that landed after the read.
        List<RefreshRead> applied = [];
        foreach (var read in reads)
        {
            if (DocumentFreshness.StampOf(read.Path) != read.Stamp)
            {
                continue;
            }

            solution = solution.WithDocumentText(
                read.DocumentId,
                SourceText.From(read.Text),
                PreservationMode.PreserveIdentity
            );
            applied.Add(read);
        }

        if (applied.Count == 0)
        {
            return solution;
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

        // Phase 3: the apply can write the refreshed text back with a fresh mtime, so re-take the
        // baseline - but only when the disk still holds exactly the applied text. Anything else (a
        // write racing the apply) keeps the old baseline and re-flags on the next scan instead of
        // being swallowed by a blind post-apply re-baseline.
        foreach (var read in applied)
        {
            try
            {
                if (File.ReadAllText(read.Path) == read.Text)
                {
                    entry.Freshness.Track(read.Path);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // The file moved under us again; keeping the old baseline re-flags it next check.
            }
        }

        return solution;
    }

    private sealed record RefreshRead(
        string Path,
        DocumentId DocumentId,
        DocumentFreshness.FileStamp Stamp,
        string Text
    );

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
