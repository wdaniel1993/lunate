using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Roslyn;

/// <summary>
/// The <c>cs_find_symbol</c> tool: definition location and signature for a type, member or
/// namespace name. Like <see cref="CsDiagnosticsTool"/>, the backend is resolved on first
/// execution so no Roslyn or MSBuild assembly loads before the first call.
/// </summary>
public sealed class CsFindSymbolTool(Func<ICSharpBackend> backendFactory) : ITool
{
    private const int InlineMatches = 10;

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Type, member or namespace name to find; use a dotted path such as CalculatorLib.Calculator.Add to disambiguate"
                }
              },
              "required": ["name"]
            }
            """
        )
        .RootElement.Clone();

    private readonly Lazy<ICSharpBackend> _backend = new(backendFactory);

    public string Name => "cs_find_symbol";

    public string Description =>
        "Find where a type or member with this name is defined and its signature — navigates large solutions without grepping";

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

        SymbolSearchResult search;
        try
        {
            search = await backend.FindSymbolAsync(name, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ToolResult($"Symbol search failed: {exception.Message}", true);
        }

        return new ToolResult(Render(load, search), IsError: false, Details: search);
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
            error = "name is required (a type, member or namespace name)";
            return false;
        }

        name = element.GetString() ?? string.Empty;
        return true;
    }

    private static string StatusText(WorkspaceLoadResult load) =>
        load.Status switch
        {
            WorkspaceStatus.NoSdk => $"No C# symbol lookup is available: {load.Message}",
            WorkspaceStatus.RestoreRequired =>
                $"C# symbol lookup needs a restore first: {load.Message}",
            WorkspaceStatus.NoSolution => $"No C# solution is available: {load.Message}",
            _ => load.Message,
        };

    private static string Render(WorkspaceLoadResult load, SymbolSearchResult search)
    {
        var builder = new StringBuilder();
        if (load.Status == WorkspaceStatus.Partial)
        {
            builder.Append("Partial load: ").AppendLine(load.Message);
        }

        if (search.Status == SymbolSearchStatus.Partial)
        {
            builder.Append("Partial search: ").AppendLine(search.Message);
        }
        else
        {
            builder.AppendLine(search.Message);
        }

        foreach (var match in search.Matches.Take(InlineMatches))
        {
            builder.AppendLine(Line(match));
        }

        if (search.Matches.Count > InlineMatches)
        {
            builder
                .Append("... and ")
                .Append(
                    (search.Matches.Count - InlineMatches).ToString(CultureInfo.InvariantCulture)
                )
                .AppendLine(" more (the full list is in the tool details).");
        }

        if (search.Truncated)
        {
            builder
                .Append("Results are truncated at ")
                .Append(SymbolCollector.MaxMatches.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" matches.");
        }

        foreach (var failure in search.Failures)
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
