using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace UiShowcaseExtension;

/// <summary>
/// Renders the <c>bars</c> tool result (model-facing <c>uikit__bars</c>): one segment line per
/// value — accent <c>█</c> glyphs repeated the value's count, then the dim numeric value.
/// Unparsable or empty input returns an empty list (the contract's degradation path: show nothing);
/// the glyph count is capped so a hostile value cannot flood the view. Presentation only: the
/// result the model sees is untouched.
/// </summary>
public sealed class UiShowcaseBarsRenderer : IToolResultRenderer
{
    /// <summary>The glyph cap per value; a larger value still shows its true number.</summary>
    internal const int MaxBarGlyphs = 40;

    private const char BarGlyph = '█';

    public string ToolName => "uikit__bars";

    public IReadOnlyList<TextSegment> Render(ToolResultView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!TryParseValues(view.ResultText, out List<int> values) || values.Count == 0)
        {
            return [];
        }

        var segments = new List<TextSegment>(values.Count * 2);
        for (var index = 0; index < values.Count; index++)
        {
            int value = values[index];
            int glyphs = Math.Clamp(value, 0, MaxBarGlyphs);
            segments.Add(new TextSegment(new string(BarGlyph, glyphs) + " ", TextStyle.Accent));
            segments.Add(
                new TextSegment(
                    value.ToString(CultureInfo.InvariantCulture)
                        + (index < values.Count - 1 ? "\n" : string.Empty),
                    TextStyle.Dim
                )
            );
        }

        return segments;
    }

    private static bool TryParseValues(string resultText, out List<int> values)
    {
        values = [];
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(resultText);
            JsonElement root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("values", out JsonElement element)
                || element.ValueKind != JsonValueKind.Array
            )
            {
                return false;
            }

            foreach (JsonElement item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out int value))
                {
                    values.Clear();
                    return false;
                }

                values.Add(value);
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
