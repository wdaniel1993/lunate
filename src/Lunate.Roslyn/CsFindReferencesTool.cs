using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Roslyn;

/// <summary>
/// The <c>cs_find_references</c> tool: every source reference to an exactly resolved symbol, so
/// the impact of an API change is visible without grepping. Like <see cref="CsFindSymbolTool"/>,
/// the backend is resolved on first execution so no Roslyn or MSBuild assembly loads before the
/// first call.
/// </summary>
public sealed class CsFindReferencesTool(Func<ICSharpBackend> backendFactory) : ITool
{
    private const int InlineReferences = 10;

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Type or member name to find references for; use a dotted path such as CalculatorLib.Calculator.Divide to disambiguate"
                }
              },
              "required": ["name"]
            }
            """
        )
        .RootElement.Clone();

    private readonly Lazy<ICSharpBackend> _backend = new(backendFactory);

    public string Name => "cs_find_references";

    public string Description =>
        "Find every source reference to a type or member with this name — checks the impact of API changes without grepping";

    public JsonElement ParametersSchema => Schema;

    public ToolAnnotations Annotations => new(ReadOnly: true);

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        if (!TryReadName(args, out var name, out var error))
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

        ReferencesResult references;
        try
        {
            references = await backend.FindReferencesAsync(name, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ToolResult($"Reference search failed: {exception.Message}", true);
        }

        return new ToolResult(Render(load, references), IsError: false, Details: references);
    }

    private static bool TryReadName(JsonElement args, out string name, out string error)
    {
        name = string.Empty;
        error = string.Empty;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = "arguments must be a JSON object";
            return false;
        }

        if (
            !args.TryGetProperty("name", out var element)
            || element.ValueKind != JsonValueKind.String
        )
        {
            error = "name is required (a type or member name)";
            return false;
        }

        name = element.GetString() ?? string.Empty;
        return true;
    }

    private static string StatusText(WorkspaceLoadResult load) =>
        load.Status switch
        {
            WorkspaceStatus.NoSdk => $"No C# reference lookup is available: {load.Message}",
            WorkspaceStatus.RestoreRequired =>
                $"C# reference lookup needs a restore first: {load.Message}",
            WorkspaceStatus.NoSolution => $"No C# solution is available: {load.Message}",
            _ => load.Message,
        };

    private static string Render(WorkspaceLoadResult load, ReferencesResult references)
    {
        var builder = new StringBuilder();
        if (load.Status == WorkspaceStatus.Partial)
        {
            builder.Append("Partial load: ").AppendLine(load.Message);
        }

        builder.AppendLine(references.Message);

        if (references.Resolved is { } resolved)
        {
            builder.Append("definition: ").AppendLine(Line(resolved));
        }
        else
        {
            foreach (var candidate in references.Candidates.Take(InlineReferences))
            {
                builder.AppendLine(Line(candidate));
            }

            if (references.Candidates.Count > InlineReferences)
            {
                builder
                    .Append("... and ")
                    .Append(
                        (references.Candidates.Count - InlineReferences).ToString(
                            CultureInfo.InvariantCulture
                        )
                    )
                    .AppendLine(" more candidate definitions (the full list is in the details).");
            }
        }

        foreach (var reference in references.References.Take(InlineReferences))
        {
            builder
                .Append("  ")
                .Append(reference.File)
                .Append(':')
                .Append(reference.Line.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(reference.Column.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }

        if (references.References.Count > InlineReferences)
        {
            builder
                .Append("... and ")
                .Append(
                    (references.References.Count - InlineReferences).ToString(
                        CultureInfo.InvariantCulture
                    )
                )
                .AppendLine(" more (the full list is in the tool details).");
        }

        if (references.Truncated)
        {
            builder
                .Append("Results are truncated at ")
                .Append(ReferencesCollector.MaxReferences.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" references.");
        }

        foreach (var failure in references.Failures)
        {
            builder.Append("Note: ").AppendLine(failure.Message);
        }

        return builder.ToString().TrimEnd();
    }

    private static string Line(SymbolMatch match) =>
        match.FromMetadata
            ? $"(metadata) {Qualified(match)} — {match.Signature}"
            : $"{match.File}:{match.Line} — {match.Signature}";

    private static string Qualified(SymbolMatch match) =>
        match.Container is { Length: > 0 } container ? $"{container}.{match.Name}" : match.Name;
}
