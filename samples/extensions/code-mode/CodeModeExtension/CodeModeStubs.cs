using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace CodeModeExtension;

/// <summary>
/// Renders the run's tool stubs into the JavaScript prelude the script reads: one
/// <c>tools["name"]</c> function per stub, each documented with JSDoc — the tool and parameter
/// descriptions, required/optional flags, enum values, defaults and degrade notes.
/// </summary>
internal static class CodeModeStubs
{
    public static string Render(IReadOnlyList<ToolStub> stubs)
    {
        var text = new StringBuilder();
        text.AppendLine("// The tools of this run; call one with `await tools[\"<name>\"](args)`.");
        text.AppendLine("const tools = {};");
        foreach (ToolStub stub in stubs)
        {
            AppendStub(text, stub);
        }

        return text.ToString();
    }

    private static void AppendStub(StringBuilder text, ToolStub stub)
    {
        text.AppendLine("/**");
        text.Append(" * ").AppendLine(Inline(stub.Description));
        text.AppendLine(" * @param {object} args Tool arguments:");
        foreach (ToolStubParameter parameter in stub.Parameters)
        {
            text.Append(" *   - ")
                .Append(parameter.Name)
                .Append(" (")
                .Append(parameter.Type)
                .Append(parameter.Required ? ", required" : ", optional")
                .Append(')');
            if (parameter.Description is { Length: > 0 } description)
            {
                text.Append(": ").Append(Inline(description));
            }

            if (parameter.Enum is { Count: > 0 } values)
            {
                text.Append("; values: ").Append(string.Join(", ", values));
            }

            if (parameter.Default is { Length: > 0 } @default)
            {
                text.Append("; default: ").Append(@default);
            }

            if (parameter.Note is { Length: > 0 } note)
            {
                text.Append("; note: ").Append(Inline(note));
            }

            text.AppendLine();
        }

        text.Append(" * @returns {Promise<string>} the tool's output text");
        if (stub.HasStructuredOutput)
        {
            text.Append("; the tool also produces structured content");
        }

        text.AppendLine();
        text.AppendLine(" */");
        string literal = JsonSerializer.Serialize(stub.Name);
        text.Append("tools[")
            .Append(literal)
            .Append("] = async (args) => __call(")
            .Append(literal)
            .Append(", args);")
            .AppendLine();
    }

    /// <summary>Keeps one line per JSDoc line and never lets a value close the comment early.</summary>
    private static string Inline(string text) =>
        text.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace("*/", "*\\/", StringComparison.Ordinal);
}
