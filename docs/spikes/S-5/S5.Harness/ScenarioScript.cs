namespace S5.Harness;

public abstract record ScriptItem
{
    public sealed record Event(AgentEvent Value) : ScriptItem;

    public sealed record Key(ConsoleKeyInfo Value) : ScriptItem;

    public sealed record Resize(int Width, int Height) : ScriptItem;
}

public readonly record struct ScriptStep(TimeSpan At, ScriptItem Item);

/// <summary>
/// The single scenario every variant replays: streaming tail (with a burst that
/// must hit the 30 fps cap), steering typed while running, an approval prompt
/// answered with "always", a tool run with the spinner, one resize, Esc cancel,
/// then Ctrl+C clear and double-quit.
/// </summary>
public static class ScenarioScript
{
    private const string Paragraph =
        "The live area keeps the streaming tail, the spinner, the footer and the " +
        "approval prompt at the bottom of the screen while finished blocks scroll above it. ";

    public static IReadOnlyList<ScriptStep> Build()
    {
        var steps = new List<ScriptStep>();

        Add(steps, 0, new ScriptItem.Event(new AgentEvent.RunStarted(Scenario.Model)));

        var words = Paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = TimeSpan.FromMilliseconds(10);
        foreach (var word in words)
        {
            Add(steps, at.TotalMilliseconds, new ScriptItem.Event(new AgentEvent.TextDelta(word + " ")));
            at += TimeSpan.FromMilliseconds(8);
        }

        Add(steps, 400, new ScriptItem.Event(new AgentEvent.TextDelta("[burst]")));
        for (var i = 0; i < 200; i++)
        {
            Add(steps, 400 + (i * 0.2), new ScriptItem.Event(new AgentEvent.TextDelta($"{i} ")));
        }

        foreach (var (key, offset) in TypeText("also check the tests", 600))
        {
            Add(steps, offset, new ScriptItem.Key(key));
        }

        Add(steps, 600 + (19 * 6) + 6, new ScriptItem.Key(Keys.Enter));
        Add(steps, 800, new ScriptItem.Event(new AgentEvent.ApprovalRequested("bash", "dotnet test")));
        Add(steps, 850, new ScriptItem.Key(Keys.Letter('a')));
        Add(steps, 900, new ScriptItem.Event(new AgentEvent.ToolStarted("bash")));
        Add(steps, 900, new ScriptItem.Event(new AgentEvent.TextDelta("running tests... ")));
        Add(steps, 1250, new ScriptItem.Event(new AgentEvent.ToolFinished("bash", true, "48 passed")));
        Add(steps, 1300, new ScriptItem.Event(new AgentEvent.Usage(1200, 340, 12.5)));
        Add(steps, 1400, new ScriptItem.Resize(100, 30));
        Add(steps, 1500, new ScriptItem.Event(new AgentEvent.ApprovalRequested("bash", "dotnet format")));
        Add(steps, 1550, new ScriptItem.Event(new AgentEvent.ToolStarted("bash")));
        Add(steps, 1800, new ScriptItem.Key(Keys.Escape));
        Add(steps, 1850, new ScriptItem.Event(new AgentEvent.RunCancelled("user")));

        foreach (var (key, offset) in TypeText("next", 2100))
        {
            Add(steps, offset, new ScriptItem.Key(key));
        }

        Add(steps, 2300, new ScriptItem.Key(Keys.CtrlC));
        Add(steps, 3400, new ScriptItem.Key(Keys.CtrlC));

        return [.. steps.OrderBy(s => s.At)];
    }

    private static IEnumerable<(ConsoleKeyInfo Key, double Offset)> TypeText(string text, double startMs)
    {
        var offset = startMs;
        foreach (var c in text)
        {
            yield return (c == ' ' ? Keys.Space : Keys.Letter(c), offset);
            offset += 6;
        }
    }

    private static void Add(List<ScriptStep> steps, double milliseconds, ScriptItem item) =>
        steps.Add(new ScriptStep(TimeSpan.FromMilliseconds(milliseconds), item));
}
