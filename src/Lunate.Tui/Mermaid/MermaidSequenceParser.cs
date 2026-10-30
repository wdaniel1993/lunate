namespace Lunate.Tui;

/// <summary>
/// Parses <c>sequenceDiagram</c>s: participants — declared with <c>participant A as Long
/// Name</c> or implied by first mention — messages over four arrows (<c>-&gt;</c>, <c>--&gt;</c>,
/// <c>-&gt;&gt;</c> and the historical <c>--&gt;&gt;</c>, solid or dashed as written) and notes
/// <c>Note over A[ and B]: text</c>. Anything else is a parse failure that names its line.
/// </summary>
internal static class MermaidSequenceParser
{
    public static bool TryParse(
        IReadOnlyList<string> source,
        out MermaidSequenceModel? model,
        out MermaidFailure? failure
    )
    {
        model = null;
        failure = null;
        if (source.All(static line => line.Trim().Length == 0))
        {
            failure = new MermaidFailure("the fence has no diagram");
            return false;
        }

        var seats = new List<MermaidParticipant>();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        List<MermaidSpeech> speech = [];
        for (var position = 0; position < source.Count; position++)
        {
            int number = position + 1;
            string statement = source[position].Trim();
            if (statement.Length == 0 || statement.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            if (!Statement(statement, number, seats, ids, speech, ref failure))
            {
                return false;
            }
        }

        model = new(seats.AsReadOnly(), speech.AsReadOnly());
        return true;
    }

    private static bool Statement(
        string statement,
        int number,
        List<MermaidParticipant> seats,
        Dictionary<string, int> ids,
        List<MermaidSpeech> speech,
        ref MermaidFailure? failure
    )
    {
        if (ids.Count == 0 && statement.Equals("sequenceDiagram", StringComparison.Ordinal))
        {
            return true;
        }

        string word = FirstWord(statement);
        switch (word)
        {
            case "sequenceDiagram" when statement.Length > word.Length:
                failure = new($"line {number}: the header cannot carry a statement");
                return false;
            case "sequenceDiagram":
                return true;
            case "participant" or "actor" when word == "actor":
                failure = new($"line {number}: unsupported statement '{word}'");
                return false;
            case "participant":
                return Participant(statement, number, seats, ids, ref failure);
            case "Note":
                return Note(statement, number, seats, ids, speech, ref failure);
            default:
                return Message(statement, number, seats, ids, speech, ref failure);
        }
    }

    private static bool Participant(
        string statement,
        int number,
        List<MermaidParticipant> seats,
        Dictionary<string, int> ids,
        ref MermaidFailure? failure
    )
    {
        string rest = statement["participant".Length..].Trim();
        int space = rest.IndexOf(' ');
        string id = space < 0 ? rest : rest[..space];
        string tail = space < 0 ? string.Empty : rest[(space + 1)..];
        if (id.Length == 0)
        {
            failure = new MermaidFailure(
                $"line {number}: a participant needs an actor: participant <id> as <name>"
            );
            return false;
        }

        string name;
        if (tail.Equals("as", StringComparison.Ordinal))
        {
            failure = new MermaidFailure(
                $"line {number}: a participant needs a display name after 'as'"
            );
            return false;
        }

        if (tail.StartsWith("as ", StringComparison.Ordinal))
        {
            name = Unquote(tail["as ".Length..].Trim());
            if (name.Length == 0)
            {
                failure = new MermaidFailure(
                    $"line {number}: a participant needs a display name after 'as'"
                );
                return false;
            }
        }
        else
        {
            name = tail.Length > 0 ? Unquote(tail) : id;
        }

        if (!ids.ContainsKey(id))
        {
            ids[id] = seats.Count;
            seats.Add(new MermaidParticipant(id, name));
        }

        return true;
    }

    private static bool Note(
        string statement,
        int number,
        List<MermaidParticipant> seats,
        Dictionary<string, int> ids,
        List<MermaidSpeech> speech,
        ref MermaidFailure? failure
    )
    {
        string rest = statement["Note".Length..].Trim();
        if (!rest.StartsWith("over", StringComparison.Ordinal))
        {
            failure = new($"line {number}: unsupported statement 'Note {FirstWord(rest)}'");
            return false;
        }

        rest = rest["over".Length..].Trim();
        int at = rest.IndexOf(':');
        if (at < 0)
        {
            failure = new($"line {number}: a note needs its text after ':'");
            return false;
        }

        var over = new List<int>();
        foreach (string raw in rest[..at].Split(','))
        {
            string id = raw.Trim();
            if (id.Length == 0 || ids.Count < 1 || ids.TryGetValue(id, out int seat) == false)
            {
                ids[id] = seats.Count;
                seats.Add(new MermaidParticipant(id, id));
                seat = ids[id];
            }

            over.Add(seat);
        }

        string content = Unquote(rest[(at + 1)..].Trim());
        if (content.Length == 0)
        {
            failure = new($"line {number}: a note needs its text after ':'");
            return false;
        }

        speech.Add(new MermaidNote(content, [.. over]));
        return true;
    }

    private static bool Message(
        string statement,
        int number,
        List<MermaidParticipant> seats,
        Dictionary<string, int> ids,
        List<MermaidSpeech> speech,
        ref MermaidFailure? failure
    )
    {
        int arrow = FirstArrow(statement, out int length, out bool dashed);
        if (arrow < 0)
        {
            failure = new($"line {number}: unsupported statement '{FirstWord(statement)}'");
            return false;
        }

        string fromId = statement[..arrow].Trim();
        string tail = statement[(arrow + length)..].Trim();
        int at = tail.IndexOf(':');
        string toId = (at < 0 ? tail : tail[..at]).Trim();
        if (!Participant(fromId, seats, ids) || !Participant(toId, seats, ids))
        {
            failure = new($"line {number}: I cannot read '{FirstWord(statement[..arrow])}'");
            return false;
        }

        if (ids[fromId] == ids[toId])
        {
            failure = new($"line {number}: a message needs two different actors");
            return false;
        }

        string content = at < 0 ? string.Empty : Unquote(tail[(at + 1)..].Trim());
        speech.Add(new MermaidMessage(ids[fromId], ids[toId], content, dashed));
        return true;
    }

    private static int FirstArrow(string statement, out int length, out bool dashed)
    {
        length = 0;
        dashed = false;
        (string Text, int Length, bool Dashed)[] arrows =
        [
            ("-->>", 4, true),
            ("->>", 3, false),
            ("-->", 3, true),
            ("->", 2, false),
        ];
        foreach ((string text, int span, bool mark) in arrows)
        {
            int at = statement.IndexOf(text, StringComparison.Ordinal);
            if (at > 0)
            {
                length = span;
                dashed = mark;
                return at;
            }
        }

        return -1;
    }

    private static bool Participant(
        string id,
        List<MermaidParticipant> seats,
        Dictionary<string, int> ids
    )
    {
        if (!id.All(static letter => char.IsLetterOrDigit(letter) || letter is '_' or '-'))
        {
            return false;
        }

        if (!ids.TryGetValue(id, out int index))
        {
            index = seats.Count;
            ids[id] = index;
            seats.Add(new MermaidParticipant(id, id));
        }

        return true;
    }

    private static string FirstWord(string text)
    {
        int end = text.IndexOf(' ');
        return end > 0 ? text[..end] : text;
    }

    private static string Unquote(string text)
    {
        if (text.Length > 1 && text[0] == '"' && text[^1] == '"')
        {
            return text[1..^1];
        }

        return text;
    }
}
