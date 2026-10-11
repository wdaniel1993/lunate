namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// Renders one named tool's result as segments. The interactive mode matches renderers by
/// <see cref="ToolName"/> (ordinal); several renderers for one name resolve to the last
/// registration. Rendering is presentation only: the output never reaches the model or the run.
/// </summary>
public interface IToolResultRenderer
{
    /// <summary>The tool name this renderer serves; matched ordinally against the tool call.</summary>
    string ToolName { get; }

    /// <summary>Renders the result into segments; returning an empty list shows nothing.</summary>
    IReadOnlyList<TextSegment> Render(ToolResultView view);
}
