using System.Text;

namespace S5.Harness;

public sealed record ScenarioResult(
    IReadOnlyList<string> Scrollback,
    IReadOnlyList<string> LiveArea,
    bool QuitRequested,
    bool CancelRequested,
    string TailText
);

public static class ScenarioRunner
{
    /// <summary>
    /// Replays the canonical script on the session's virtual clock and returns
    /// a deterministic transcript. Because every variant shares the state
    /// machine and renderer, transcripts are expected to be byte-identical.
    /// </summary>
    public static ScenarioResult Run(ISession session, IReadOnlyList<ScriptStep>? script = null)
    {
        script ??= ScenarioScript.Build();
        var now = TimeSpan.Zero;

        foreach (var step in script)
        {
            session.Advance(step.At - now);
            now = step.At;
            session.DrainAsync().AsTask().GetAwaiter().GetResult();

            switch (step.Item)
            {
                case ScriptItem.Event e:
                    session.Post(e.Value);
                    break;
                case ScriptItem.Key k:
                    session.Key(k.Value);
                    break;
                case ScriptItem.Resize r:
                    session.Resize(r.Width, r.Height);
                    break;
            }
        }

        session.Advance(TimeSpan.FromMilliseconds(150));
        now += TimeSpan.FromMilliseconds(150);
        session.DrainAsync().AsTask().GetAwaiter().GetResult();

        var snapshot = session.Snapshot();
        return new ScenarioResult(
            [.. session.Terminal.Scrollback],
            [.. snapshot],
            session.IsQuitRequested,
            session.IsCancelRequested,
            session.TailText
        );
    }

    public static string ToTranscript(ScenarioResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("== scrollback ==");
        foreach (var block in result.Scrollback)
        {
            text.AppendLine(block);
        }

        text.AppendLine("== live area ==");
        foreach (var line in result.LiveArea)
        {
            text.AppendLine(line);
        }

        text.AppendLine(
            $"quit={result.QuitRequested.ToString().ToLowerInvariant()} "
                + $"cancel={result.CancelRequested.ToString().ToLowerInvariant()}"
        );
        return text.ToString();
    }
}
