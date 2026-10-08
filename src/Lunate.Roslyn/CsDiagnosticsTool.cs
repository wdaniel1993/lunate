using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Roslyn;

/// <summary>
/// The <c>cs_diagnostics</c> tool: compiler errors and warnings for changed files (default) or the
/// whole solution, without a full build. The backend is resolved on first execution so no Roslyn
/// or MSBuild assembly loads before the first call.
/// </summary>
public sealed class CsDiagnosticsTool(Func<ICSharpBackend> backendFactory) : ITool
{
    private const int InlineItems = 10;

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "scope": {
                  "type": "string",
                  "enum": ["changed", "solution"],
                  "description": "Which files to check: \"changed\" (default) checks files edited since the last check; \"solution\" checks every document"
                }
              }
            }
            """
        )
        .RootElement.Clone();

    private readonly Lazy<ICSharpBackend> _backend = new(backendFactory);

    public string Name => "cs_diagnostics";

    public string Description =>
        "Compiler errors and warnings for files changed since the last check (or the whole solution) — fast compile check after edits without a full dotnet build";

    public JsonElement ParametersSchema => Schema;

    public ToolAnnotations Annotations => new(ReadOnly: true);

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        if (!TryReadScope(args, out var scope, out var error))
        {
            return new ToolResult(error, IsError: true);
        }

        ICSharpBackend backend;
        WorkspaceLoadResult load;
        try
        {
            backend = _backend.Value;
            load = await backend.LoadAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ToolResult($"The C# backend could not start: {exception.Message}", true);
        }

        if (
            load.Status
            is WorkspaceStatus.NoSdk
                or WorkspaceStatus.RestoreRequired
                or WorkspaceStatus.NoSolution
        )
        {
            return new ToolResult(StatusText(load), IsError: true, Details: load);
        }

        DiagnosticsResult diagnostics;
        try
        {
            diagnostics = await backend.GetDiagnosticsAsync(scope, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ToolResult($"Diagnostics failed: {exception.Message}", true);
        }

        return new ToolResult(Render(load, diagnostics), IsError: false, Details: diagnostics);
    }

    private static bool TryReadScope(JsonElement args, out DiagnosticsScope scope, out string error)
    {
        scope = DiagnosticsScope.ChangedFiles;
        error = string.Empty;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = "arguments must be a JSON object";
            return false;
        }

        if (!args.TryGetProperty("scope", out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = "scope must be \"changed\" or \"solution\"";
            return false;
        }

        switch (element.GetString())
        {
            case "changed":
                scope = DiagnosticsScope.ChangedFiles;
                return true;
            case "solution":
                scope = DiagnosticsScope.Solution;
                return true;
            default:
                error = "scope must be \"changed\" or \"solution\"";
                return false;
        }
    }

    private static string StatusText(WorkspaceLoadResult load) =>
        load.Status switch
        {
            WorkspaceStatus.NoSdk => $"No C# diagnostics are available: {load.Message}",
            WorkspaceStatus.RestoreRequired =>
                $"C# diagnostics need a restore first: {load.Message}",
            WorkspaceStatus.NoSolution => $"No C# solution is available: {load.Message}",
            _ => load.Message,
        };

    private static string Render(WorkspaceLoadResult load, DiagnosticsResult diagnostics)
    {
        var builder = new StringBuilder();
        if (load.Status == WorkspaceStatus.Partial)
        {
            builder.Append("Partial load: ").AppendLine(load.Message);
        }

        if (diagnostics.ErrorCount == 0 && diagnostics.WarningCount == 0)
        {
            builder
                .Append("No compiler errors or warnings in ")
                .Append(diagnostics.ScopeDescription)
                .AppendLine(".");
        }
        else
        {
            builder
                .Append(Count(diagnostics.ErrorCount, "error"))
                .Append(", ")
                .Append(Count(diagnostics.WarningCount, "warning"))
                .Append(" in ")
                .Append(diagnostics.ScopeDescription)
                .AppendLine(".");
        }

        foreach (var item in diagnostics.Items.Take(InlineItems))
        {
            builder
                .Append(item.File)
                .Append('(')
                .Append(item.Line.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(item.Column.ToString(CultureInfo.InvariantCulture))
                .Append("): ")
                .Append(SeverityName(item.Severity))
                .Append(' ')
                .Append(item.Id)
                .Append(": ")
                .AppendLine(item.Message);
        }

        if (diagnostics.Items.Count > InlineItems)
        {
            builder
                .Append("... and ")
                .Append(
                    (diagnostics.Items.Count - InlineItems).ToString(CultureInfo.InvariantCulture)
                )
                .AppendLine(" more (the full list is in the tool details).");
        }

        if (diagnostics.Truncated)
        {
            builder
                .Append("Results are truncated at ")
                .Append(DiagnosticsCollector.MaxItems.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" items.");
        }

        foreach (var failure in diagnostics.Failures)
        {
            builder.Append("Note: ").AppendLine(failure.Message);
        }

        return builder.ToString().TrimEnd();
    }

    private static string Count(int count, string noun)
    {
        var number = count.ToString(CultureInfo.InvariantCulture);
        return count == 1 ? $"{number} {noun}" : $"{number} {noun}s";
    }

    private static string SeverityName(DiagnosticsSeverity severity) =>
        severity == DiagnosticsSeverity.Error ? "error" : "warning";
}
