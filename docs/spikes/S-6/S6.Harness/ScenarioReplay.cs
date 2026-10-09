using System.Diagnostics;
using System.Text;

namespace S6.Harness;

public sealed record ScenarioResult(
    string RawOutput,
    string ScreenText,
    IReadOnlyList<string> FinishedBlocks,
    bool QuitRequested,
    bool CancelRequested,
    string TailText,
    string? Status,
    IReadOnlyList<string> KeyLog,
    int Ticks
);

/// <summary>
/// Replays the canonical S-5 script against the XenoAtom live loop in real
/// time. XenoAtom's host loop is deadline/event driven and exposes no public
/// virtual clock (its deterministic <c>Tick</c> hook is internal), so the
/// replay waits on wall-clock script offsets.
/// </summary>
public static class ScenarioReplay
{
    public static async Task<ScenarioResult> RunAsync(
        XenoLiveApp app,
        IReadOnlyList<ScriptStep>? script = null,
        CancellationToken cancellationToken = default
    )
    {
        script ??= ScenarioScript.Build();
        var live = Task.Run(() => app.RunAsync(cancellationToken), cancellationToken);
        var clock = Stopwatch.StartNew();

        foreach (var step in script)
        {
            var wait = step.At - clock.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            Dispatch(app, step.Item);
        }

        var timeout = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        if (await Task.WhenAny(live, timeout) == timeout)
        {
            app.RequestStop();
            await live;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);

        var raw = app.GetOutputText();
        var screenText = LiveVisual.RenderSnapshot(
            app.Model,
            Scenario.InitialWidth + 20,
            Scenario.InitialHeight + 6
        );

        return new ScenarioResult(
            raw,
            screenText,
            [.. app.FinishedBlocks],
            app.Model.QuitRequested,
            app.Model.CancelRequested,
            app.Model.TailText,
            app.Model.Status,
            [.. app.Visual.KeyLog],
            app.Ticks
        );
    }

    public static string ToTranscript(ScenarioResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("== finished blocks (Terminal.Write + MarkdownControl) ==");
        foreach (var block in result.FinishedBlocks)
        {
            text.AppendLine(block);
            text.AppendLine(LiveVisual.RenderMarkdown(block, 60, 4));
        }

        text.AppendLine("== live area (final state, one-shot render) ==");
        text.AppendLine(result.ScreenText);
        text.AppendLine("== state ==");
        text.AppendLine(
            $"quit={result.QuitRequested.ToString().ToLowerInvariant()} "
                + $"cancel={result.CancelRequested.ToString().ToLowerInvariant()} "
                + $"ticks={result.Ticks}"
        );
        text.AppendLine($"tail_len={result.TailText.Length}");
        text.AppendLine($"status={result.Status}");
        text.AppendLine($"keys={result.KeyLog.Count}");
        foreach (var key in result.KeyLog.TakeLast(8))
        {
            text.AppendLine($"  {key}");
        }

        return text.ToString();
    }

    private static void Dispatch(XenoLiveApp app, ScriptItem item)
    {
        switch (item)
        {
            case ScriptItem.Event e:
                app.PostEvent(e.Value);
                break;
            case ScriptItem.Key k:
                app.SendKey(k.Value);
                break;
            case ScriptItem.Resize r:
                app.Resize(r.Width, r.Height);
                break;
        }
    }
}
