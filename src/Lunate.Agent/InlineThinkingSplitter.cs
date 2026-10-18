using System.Text;

namespace Lunate.Agent;

/// <summary>
/// Streaming splitter for models that embed their reasoning in the text stream between a
/// lowercase open tag and its closing tag (reasoning style <c>inline</c>, design D4 of the
/// reasoning-end-to-end change). The tags themselves are never emitted; a tag split across
/// fragments is still recognized because the splitter holds back any tail that could grow into
/// a tag (at most one character short of the longest tag). At stream end <see cref="Flush"/>
/// releases the held tail: unclosed reasoning stays reasoning.
/// </summary>
internal sealed class InlineThinkingSplitter
{
    private const string OpenTag = "\u003Cthink>";
    private const string CloseTag = "\u003C/think>";

    /// <summary>One piece of the stream: reasoning content or plain text, tags excluded.</summary>
    internal readonly record struct Segment(bool IsReasoning, string Text);

    private readonly StringBuilder _pending = new();
    private bool _inside;

    /// <summary>Consumes one text fragment and returns every segment decidable from it.</summary>
    internal IReadOnlyList<Segment> Feed(string fragment)
    {
        _pending.Append(fragment);
        return Drain(holdTail: true);
    }

    /// <summary>
    /// Releases everything still buffered at stream end: held tail characters become content of
    /// the current state, so an unclosed open tag leaves the remainder reasoning.
    /// </summary>
    internal IReadOnlyList<Segment> Flush() => Drain(holdTail: false);

    private List<Segment> Drain(bool holdTail)
    {
        List<Segment> segments = [];
        while (true)
        {
            string tag = _inside ? CloseTag : OpenTag;
            int index = _pending.ToString().IndexOf(tag, StringComparison.Ordinal);
            if (index >= 0)
            {
                Add(segments, _inside, _pending.ToString(0, index));
                _pending.Remove(0, index + tag.Length);
                _inside = !_inside;
                continue;
            }

            int hold = holdTail ? HeldTail(tag) : 0;
            Add(segments, _inside, _pending.ToString(0, _pending.Length - hold));
            _pending.Remove(0, _pending.Length - hold);
            return segments;
        }
    }

    /// <summary>
    /// Length of the longest suffix of the buffer that is a proper prefix of
    /// <paramref name="tag"/> — the characters a tag split across fragments may need next.
    /// </summary>
    private int HeldTail(string tag)
    {
        int max = Math.Min(tag.Length - 1, _pending.Length);
        for (int length = max; length > 0; length--)
        {
            if (
                string.CompareOrdinal(_pending.ToString(), _pending.Length - length, tag, 0, length)
                == 0
            )
            {
                return length;
            }
        }

        return 0;
    }

    private static void Add(List<Segment> segments, bool reasoning, string text)
    {
        if (text.Length > 0)
        {
            segments.Add(new Segment(reasoning, text));
        }
    }
}
