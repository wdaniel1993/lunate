using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace Spike.WorkspaceProbe;

internal static class Load
{
    public static Task<int> RunAsync(string[] args)
    {
        var options = LoadOptions.Parse(args);
        return RunCoreAsync(options);
    }

    private static async Task<int> RunCoreAsync(LoadOptions options)
    {
        Locator.EnsureRegistered();

        Console.WriteLine("probe: load");
        Console.WriteLine($"label: {options.Label}");
        Console.WriteLine($"solution: {options.SolutionPath}");

        var loadTimes = new List<double>();
        for (var run = 1; run <= options.Runs; run++)
        {
            var failures = new List<string>();
            using var workspace = MSBuildWorkspace.Create();
            workspace.RegisterWorkspaceFailedHandler(e =>
                failures.Add($"{e.Diagnostic.Kind}: {e.Diagnostic.Message}")
            );

            var stopwatch = Stopwatch.StartNew();
            var solution = await workspace
                .OpenSolutionAsync(options.SolutionPath)
                .ConfigureAwait(false);
            stopwatch.Stop();

            var documents = solution.Projects.Sum(project => project.DocumentIds.Count);
            loadTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            Console.WriteLine(
                $"run: {run} load_ms={Ms(stopwatch.Elapsed.TotalMilliseconds)} "
                    + $"projects={solution.ProjectIds.Count} documents={documents} workspace_failures={failures.Count}"
            );

            foreach (var failure in failures.Take(20))
            {
                Console.WriteLine($"workspace_failure: {failure}");
            }

            if (options.Diagnostics)
            {
                await PrintDiagnosticsAsync(solution, options.DiagProjects).ConfigureAwait(false);
            }

            if (options.EditFile is not null && run == options.Runs)
            {
                await MeasureEditsAsync(solution, options).ConfigureAwait(false);
            }
        }

        Console.WriteLine($"load_median_ms: {Ms(Stats.Median(loadTimes))}");
        return 0;
    }

    private static async Task PrintDiagnosticsAsync(Solution solution, int maxProjects)
    {
        var projects = solution
            .Projects.OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var project in projects.Take(maxProjects))
        {
            var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            if (compilation is null)
            {
                Console.WriteLine($"diagnostics: project={project.Name} compilation=<null>");
                continue;
            }

            var diagnostics = compilation.GetDiagnostics();
            var summary = DiagnosticsSummary.From(diagnostics);
            var firstErrors = diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Id)
                .Distinct(StringComparer.Ordinal)
                .Take(8);
            Console.WriteLine(
                $"diagnostics: project={project.Name} {summary} error_ids=[{string.Join(",", firstErrors)}]"
            );
        }
    }

    private static async Task MeasureEditsAsync(Solution solution, LoadOptions options)
    {
        var document = solution
            .Projects.SelectMany(project => project.Documents)
            .FirstOrDefault(doc =>
                doc.FilePath is not null
                && doc.FilePath.EndsWith(options.EditFile!, StringComparison.Ordinal)
            );
        if (document?.FilePath is null)
        {
            Console.WriteLine($"edit: no document matching {options.EditFile}");
            return;
        }

        var filePath = document.FilePath;
        var original = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        if (!original.EndsWith('\n'))
        {
            original += "\n";
        }

        Console.WriteLine($"edit_file: {filePath}");
        var currentSolution = solution;
        var currentText = original;
        var durations = new List<double>();
        for (var iteration = 1; iteration <= options.EditIterations; iteration++)
        {
            var inject = !EditScript.HasError(currentText);
            var editedText = EditScript.Toggle(currentText);

            var total = Stopwatch.StartNew();
            await File.WriteAllTextAsync(filePath, editedText).ConfigureAwait(false);
            var apply = Stopwatch.StartNew();
            var editedDocument = currentSolution.GetDocument(document.Id)!;
            var changedDocument = editedDocument.WithText(
                SourceText.From(editedText, Encoding.UTF8)
            );
            var compilation = await changedDocument
                .Project.GetCompilationAsync()
                .ConfigureAwait(false);
            var diagnostics = compilation!.GetDiagnostics();
            apply.Stop();
            total.Stop();

            var summary = DiagnosticsSummary.From(diagnostics);
            durations.Add(total.Elapsed.TotalMilliseconds);
            Console.WriteLine(
                $"edit_cycle: iteration={iteration} inject={inject} total_ms={Ms(total.Elapsed.TotalMilliseconds)} "
                    + $"apply_ms={Ms(apply.Elapsed.TotalMilliseconds)} {summary}"
            );

            currentSolution = changedDocument.Project.Solution;
            currentText = editedText;
        }

        await File.WriteAllTextAsync(filePath, original).ConfigureAwait(false);
        Console.WriteLine("edit_restore: original_written=true");
        Console.WriteLine($"edit_median_ms: {Ms(Stats.Median(durations))}");
    }

    private static string Ms(double milliseconds) =>
        milliseconds.ToString("0.0", CultureInfo.InvariantCulture);

    private sealed record LoadOptions(
        string SolutionPath,
        int Runs,
        string? EditFile,
        int EditIterations,
        bool Diagnostics,
        int DiagProjects,
        string Label
    )
    {
        public static LoadOptions Parse(string[] args)
        {
            if (args.Length == 0)
            {
                throw new ArgumentException(
                    "usage: load <solution> [--runs N] [--file <substring>] [--edit-iterations N] [--diagnostics] [--diag-projects N] [--label <text>]"
                );
            }

            var solutionPath = Path.GetFullPath(args[0]);
            var runs = 1;
            string? editFile = null;
            var editIterations = 3;
            var diagnostics = false;
            var diagProjects = int.MaxValue;
            var label = Path.GetFileName(solutionPath);
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--runs":
                        runs = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--file":
                        editFile = args[++i];
                        break;
                    case "--edit-iterations":
                        editIterations = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--diagnostics":
                        diagnostics = true;
                        break;
                    case "--diag-projects":
                        diagProjects = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--label":
                        label = args[++i];
                        break;
                    default:
                        throw new ArgumentException($"unknown option: {args[i]}");
                }
            }

            return new LoadOptions(
                solutionPath,
                runs,
                editFile,
                editIterations,
                diagnostics,
                diagProjects,
                label
            );
        }
    }
}
