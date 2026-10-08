using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Roslyn;

/// <summary>
/// The <c>cs_outline</c> tool: one file's types and member signatures without bodies, so a big
/// file can be understood cheaply. The backend is resolved on first execution but never loaded —
/// outlining is syntax-level and works without a solution.
/// </summary>
public sealed class CsOutlineTool(Func<ICSharpBackend> backendFactory) : ITool
{
    private const int InlineItems = 40;

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "file": {
                  "type": "string",
                  "description": "Source file to outline, relative to the worktree root (or an absolute path inside it)"
                }
              },
              "required": ["file"]
            }
            """
        )
        .RootElement.Clone();

    private readonly Lazy<ICSharpBackend> _backend = new(backendFactory);

    public string Name => "cs_outline";

    public string Description =>
        "List a file's types and member signatures without bodies — cheap context for big files";

    public JsonElement ParametersSchema => Schema;

    public ToolAnnotations Annotations => new(ReadOnly: true);

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        if (!TryReadFile(args, out var file, out var error))
        {
            return new ToolResult(error, IsError: true);
        }

        ICSharpBackend backend;
        OutlineResult outline;
        try
        {
            backend = _backend.Value;
            outline = await backend.OutlineAsync(file, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ToolResult($"Outline failed: {exception.Message}", true);
        }

        return new ToolResult(Render(outline), IsError: false, Details: outline);
    }

    private static bool TryReadFile(JsonElement args, out string file, out string error)
    {
        file = string.Empty;
        error = string.Empty;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = "arguments must be a JSON object";
            return false;
        }

        if (
            !args.TryGetProperty("file", out var element)
            || element.ValueKind != JsonValueKind.String
        )
        {
            error = "file is required (a source file to outline)";
            return false;
        }

        file = element.GetString() ?? string.Empty;
        return true;
    }

    private static string Render(OutlineResult outline)
    {
        var builder = new StringBuilder();
        builder.AppendLine(outline.Message);

        foreach (var item in outline.Items.Take(InlineItems))
        {
            var depth = item.Container is null ? 0 : item.Container.Split('.').Length;
            builder
                .Append(' ', depth * 2)
                .Append(item.Signature)
                .Append(" (line ")
                .Append(item.Line.ToString(CultureInfo.InvariantCulture))
                .AppendLine(")");
        }

        if (outline.Items.Count > InlineItems)
        {
            builder
                .Append("... and ")
                .Append((outline.Items.Count - InlineItems).ToString(CultureInfo.InvariantCulture))
                .AppendLine(" more (the full list is in the tool details).");
        }

        if (outline.Truncated)
        {
            builder
                .Append("Results are truncated at ")
                .Append(OutlineCollector.MaxItems.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" items.");
        }

        return builder.ToString().TrimEnd();
    }
}
