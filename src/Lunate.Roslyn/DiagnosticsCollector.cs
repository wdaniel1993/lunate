using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Lunate.Roslyn;

internal static class DiagnosticsCollector
{
    internal const int MaxItems = 200;

    /// <summary>
    /// Compiles <paramref name="projects"/> and collects compiler errors and warnings with source
    /// positions. When <paramref name="onlyFiles"/> is set (canonical paths), only diagnostics in
    /// those files are kept.
    /// </summary>
    public static async Task<CollectedDiagnostics> CollectAsync(
        IEnumerable<Project> projects,
        IReadOnlySet<string>? onlyFiles,
        string root,
        CancellationToken ct
    )
    {
        var items = new CappedList<DiagnosticsItem>(MaxItems);
        var errors = 0;
        var warnings = 0;
        Dictionary<SyntaxTree, string> filePaths = [];

        foreach (var project in projects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            foreach (var diagnostic in compilation.GetDiagnostics(ct))
            {
                if (
                    diagnostic.Severity
                    is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                )
                {
                    continue;
                }

                if (
                    !diagnostic.Location.IsInSource
                    || diagnostic.Location.SourceTree is not { } tree
                )
                {
                    continue;
                }

                if (!filePaths.TryGetValue(tree, out var filePath))
                {
                    filePath = PathIdentity.Canonicalize(tree.FilePath);
                    filePaths[tree] = filePath;
                }

                if (onlyFiles is not null && !onlyFiles.Contains(filePath))
                {
                    continue;
                }

                var severity =
                    diagnostic.Severity == DiagnosticSeverity.Error
                        ? DiagnosticsSeverity.Error
                        : DiagnosticsSeverity.Warning;
                if (severity == DiagnosticsSeverity.Error)
                {
                    errors++;
                }
                else
                {
                    warnings++;
                }

                var position = diagnostic.Location.GetLineSpan().StartLinePosition;
                items.Add(
                    new DiagnosticsItem(
                        PathIdentity.RelativeOrAbsolute(root, filePath),
                        position.Line + 1,
                        position.Character + 1,
                        severity,
                        diagnostic.Id,
                        diagnostic.GetMessage(CultureInfo.InvariantCulture)
                    )
                );
            }
        }

        return new CollectedDiagnostics(items, errors, warnings);
    }
}

internal sealed record CollectedDiagnostics(
    CappedList<DiagnosticsItem> Items,
    int ErrorCount,
    int WarningCount
);
