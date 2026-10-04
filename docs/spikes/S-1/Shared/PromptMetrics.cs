using Microsoft.Extensions.AI;

namespace Spike.Shared;

public sealed record PromptSurface(
    int InstructionsChars,
    int SystemMessageChars,
    IReadOnlyList<(string Name, int Chars)> Tools)
{
    public int TotalChars => this.InstructionsChars + this.SystemMessageChars + this.Tools.Sum(t => t.Chars);

    public int EstimatedTokens => (this.TotalChars + 3) / 4;

    public static PromptSurface Measure(RecordedRequest request)
    {
        int instructions = request.Options?.Instructions?.Length ?? 0;
        int system = request.Messages
            .Where(m => m.Role == ChatRole.System)
            .Sum(m => m.Text.Length);

        List<(string, int)> tools = [];
        foreach (AIFunctionDeclaration tool in (request.Options?.Tools ?? []).OfType<AIFunctionDeclaration>())
        {
            tools.Add((tool.Name, (tool.Description?.Length ?? 0) + tool.JsonSchema.GetRawText().Length));
        }

        return new PromptSurface(instructions, system, tools);
    }

    public IEnumerable<string> Lines()
    {
        yield return $"prompt_instructions_chars={this.InstructionsChars}";
        yield return $"prompt_system_message_chars={this.SystemMessageChars}";
        foreach ((string name, int chars) in this.Tools)
        {
            yield return $"prompt_tool_chars name={name} chars={chars}";
        }

        yield return $"prompt_total_chars={this.TotalChars}";
        yield return $"prompt_estimated_tokens={this.EstimatedTokens}";
    }
}
