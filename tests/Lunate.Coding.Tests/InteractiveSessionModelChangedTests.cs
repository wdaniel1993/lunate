using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>ModelChanged</c> emission at the session model switch: both paths (<c>/model &lt;id&gt;</c>
/// and the picker) converge on <c>SwitchToModel</c>, which fires the hook with the new model id and
/// an empty run id (the switch happens outside a run).
/// </summary>
public sealed class InteractiveSessionModelChangedTests
{
    [Fact]
    public async Task Model_id_switch_emits_ModelChanged_with_the_new_id_and_empty_run_id()
    {
        var runner = new HookRunner();
        var handler = new RecordingHandler();
        runner.Register("ext.test", handler);
        using var host = new InteractiveSessionHost(hooks: runner);
        Task run = host.RunAsync();

        host.Console.SendText("/model gpt-4o");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: gpt-4o");
        await host.WaitUntilAsync(() => handler.Seen.Count == 1);

        ModelChangedPayload payload = Assert.Single(handler.Seen);
        Assert.Equal("gpt-4o", payload.Model);
        Assert.Equal(string.Empty, payload.RunId);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Picker_confirm_emits_ModelChanged_with_the_chosen_id()
    {
        var runner = new HookRunner();
        var handler = new RecordingHandler();
        runner.Register("ext.test", handler);
        using var host = new InteractiveSessionHost(hooks: runner);
        SettingsStore.WriteFavorites(["gpt-4o-mini", "gpt-4o"], host.Temp.File("settings.json"));
        Task run = host.RunAsync();

        host.Console.Send(
            new KeyEvent(KeyKind.Character, "p", Ctrl: false, Shift: false, Alt: true)
        );
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select model");
        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "> gpt-4o (openai)");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: gpt-4o");
        await host.WaitUntilAsync(() => handler.Seen.Count == 1);

        ModelChangedPayload payload = Assert.Single(handler.Seen);
        Assert.Equal("gpt-4o", payload.Model);
        Assert.Equal(string.Empty, payload.RunId);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task An_unknown_model_id_emits_nothing()
    {
        var runner = new HookRunner();
        var handler = new RecordingHandler();
        runner.Register("ext.test", handler);
        using var host = new InteractiveSessionHost(hooks: runner);
        Task run = host.RunAsync();

        host.Console.SendText("/model nope");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "unknown model: nope");

        Assert.Empty(handler.Seen);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_model_command_emits_ModelChanged_with_the_chosen_id()
    {
        var runner = new HookRunner();
        var handler = new RecordingHandler();
        runner.Register("ext.test", handler);
        using var host = new InteractiveSessionHost(
            hooks: runner,
            launch: new LaunchOptions { Model = "gpt-4o-mini" }
        );
        SettingsStore.WriteFavorites(["gpt-4o"], host.Temp.File("settings.json"));
        Task run = host.RunAsync();

        host.Console.SendText("/model gpt-4o");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: gpt-4o");
        await host.WaitUntilAsync(() => handler.Seen.Count == 1);

        ModelChangedPayload payload = Assert.Single(handler.Seen);
        Assert.Equal("gpt-4o", payload.Model);
        Assert.Equal(string.Empty, payload.RunId);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Without_a_hook_runner_the_switch_still_works()
    {
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { NoExtensions = true }
        );
        Task run = host.RunAsync();

        host.Console.SendText("/model gpt-4o");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: gpt-4o");

        Session session = Session.Load(
            Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl"))
        );
        SessionModelChangeEntry change = Assert.Single(
            session.Entries.OfType<SessionModelChangeEntry>()
        );
        Assert.Equal("gpt-4o", change.Model);

        host.Console.Complete();
        await run;
    }

    private sealed class RecordingHandler : IModelChangedHandler
    {
        public int Priority => 0;

        public List<ModelChangedPayload> Seen { get; } = [];

        public ValueTask ObserveAsync(
            ModelChangedPayload payload,
            CancellationToken cancellationToken
        )
        {
            Seen.Add(payload);
            return ValueTask.CompletedTask;
        }
    }
}
