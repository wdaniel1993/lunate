using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Roslyn;

/// <summary>
/// The <c>cs_rename</c> tool: a solution-wide rename planned as file edits, never applied — the
/// plan goes through the existing edit/approval path. Like <see cref="CsFindSymbolTool"/>, the
/// backend is resolved on first execution.
/// </summary>
public sealed class CsRenameTool(Func<ICSharpBackend> backendFactory) : ITool
{
    private const int InlineChanges = 10;
    private const string NothingChanged = "nothing was changed — apply via edit/write";

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Type or member name to rename; use a dotted path such as CalculatorLib.Calculator.Divide to disambiguate"
                },
                "newName": {
                  "type": "string",
                  "description": "The new name; must be a valid C# identifier"
                }
              },
              "required": ["name", "newName"]
            }
            """
        )
        .RootElement.Clone();

    private readonly Lazy<ICSharpBackend> _backend = new(backendFactory);

    public string Name => "cs_rename";

    public string Description =>
        "Plan a solution-wide rename of a type or member as file edits — nothing is changed until you apply them with edit/write";

    public JsonElement ParametersSchema => Schema;

    public ToolAnnotations Annotations => new(ReadOnly: true);

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        if (!TryReadNames(args, out var name, out var newName, out var error))
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

        RenamePlanResult plan;
        try
        {
            plan = await backend.PlanRenameAsync(name, newName, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ToolResult($"Rename planning failed: {exception.Message}", true);
        }

        var isError =
            plan.Status
            is SymbolSearchStatus.NoSdk
                or SymbolSearchStatus.RestoreRequired
                or SymbolSearchStatus.NoSolution;
        return new ToolResult(Render(load, plan), IsError: isError, Details: plan);
    }

    private static bool TryReadNames(
        JsonElement args,
        out string name,
        out string newName,
        out string error
    )
    {
        name = string.Empty;
        newName = string.Empty;
        error = string.Empty;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = "arguments must be a JSON object";
            return false;
        }

        if (
            !args.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
        )
        {
            error = "name is required (a type or member name)";
            return false;
        }

        if (
            !args.TryGetProperty("newName", out var newNameElement)
            || newNameElement.ValueKind != JsonValueKind.String
        )
        {
            error = "newName is required (the new C# identifier)";
            return false;
        }

        name = nameElement.GetString() ?? string.Empty;
        newName = newNameElement.GetString() ?? string.Empty;
        return true;
    }

    private static string StatusText(WorkspaceLoadResult load) =>
        load.Status switch
        {
            WorkspaceStatus.NoSdk => $"No C# rename planning is available: {load.Message}",
            WorkspaceStatus.RestoreRequired =>
                $"C# rename planning needs a restore first: {load.Message}",
            WorkspaceStatus.NoSolution => $"No C# solution is available: {load.Message}",
            _ => load.Message,
        };

    private static string Render(WorkspaceLoadResult load, RenamePlanResult plan)
    {
        var builder = new StringBuilder();
        if (load.Status == WorkspaceStatus.Partial)
        {
            builder.Append("Partial load: ").AppendLine(load.Message);
        }

        builder.AppendLine(plan.Message);

        foreach (
            var (file, entry) in plan
                .Changes.SelectMany(change =>
                    change.Entries.Select(entry => (change.File, Entry: entry))
                )
                .Take(InlineChanges)
        )
        {
            builder
                .Append(file)
                .Append(':')
                .Append(entry.Line.ToString(CultureInfo.InvariantCulture))
                .Append(": ")
                .Append(entry.OldText)
                .Append(" → ")
                .AppendLine(entry.NewText);
        }

        if (plan.TotalChangeCount > InlineChanges)
        {
            builder
                .Append("... and ")
                .Append(
                    (plan.TotalChangeCount - InlineChanges).ToString(CultureInfo.InvariantCulture)
                )
                .AppendLine(" more changes (the full plan is in the tool details).");
        }

        if (plan.Truncated)
        {
            builder
                .Append("Results are truncated at ")
                .Append(RenamePlanner.MaxChangeEntries.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" changes.");
        }

        if (!plan.Message.Contains("nothing was changed", StringComparison.Ordinal))
        {
            builder.AppendLine(NothingChanged);
        }

        foreach (var failure in plan.Failures)
        {
            builder.Append("Note: ").AppendLine(failure.Message);
        }

        return builder.ToString().TrimEnd();
    }
}
