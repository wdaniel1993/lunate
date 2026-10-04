using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Spike.OwnLoop;
using Spike.Shared;

string mode = args.Length > 0 ? args[0] : "all";

const string SystemPrompt = """
You are Lunate, a coding agent working in the user's repository at /repo on macOS, shell bash.
Tools: read, write, edit, bash.
- Read a file before you edit it. Prefer edit over write for existing files.
- Keep old_text in edit short but unique; include start_line if the text repeats.
- After changing code, build or run the relevant tests and report the result.
- Never touch files outside the repository. Ask before destructive commands.
- Be brief. Show what you changed and why, not every step.
""";

switch (mode)
{
    case "probe":
        await ProbeAsync();
        break;
    case "check2":
        RunCheck2();
        break;
    case "check3":
        await RunCheck3Async();
        break;
    case "check4":
        await RunCheck4Async();
        break;
    case "check5":
        await RunCheck5Async();
        break;
    case "split":
        await RunSplitAsync();
        break;
    default:
        Console.Error.WriteLine($"Unknown mode '{mode}'.");
        Environment.ExitCode = 2;
        break;
}

static async Task ProbeAsync()
{
    DateTime processStart = System.Diagnostics.Process.GetCurrentProcess().StartTime;
    var buildWatch = System.Diagnostics.Stopwatch.StartNew();
    ScriptedChatClient client = new([new([Updates.Text("ready")])]);
    OwnLoopAgent agent = new(client, SpikeTool.All, SystemPrompt, (_, _) => ValueTask.FromResult(ApprovalDecision.AllowOnce));
    buildWatch.Stop();

    Console.WriteLine($"agent_build_ms={buildWatch.Elapsed.TotalMilliseconds:F1}");
    Console.WriteLine($"startup_ms={(DateTime.Now - processStart).TotalMilliseconds:F1}");

    var firstRunWatch = System.Diagnostics.Stopwatch.StartNew();
    _ = await CollectAsync(agent.RunAsync("hello"));
    firstRunWatch.Stop();
    Console.WriteLine($"first_run_ms={firstRunWatch.Elapsed.TotalMilliseconds:F1}");

    Console.WriteLine($"managed_heap_bytes={GC.GetTotalMemory(forceFullCollection: true)}");
    Console.WriteLine($"pid={Environment.ProcessId}");
    Console.WriteLine("READY");
    await Task.Delay(TimeSpan.FromSeconds(8));
}

static void RunCheck2()
{
    ScriptedChatClient client = new([new([Updates.Text("ok")])]);
    OwnLoopAgent agent = new(client, SpikeTool.All, SystemPrompt, (_, _) => ValueTask.FromResult(ApprovalDecision.AllowOnce));
    _ = CollectAsync(agent.RunAsync("hello")).GetAwaiter().GetResult();

    Console.WriteLine("--- prompt surface (own loop) ---");
    foreach (string line in PromptSurface.Measure(client.Requests[0]).Lines())
    {
        Console.WriteLine(line);
    }
}

static async Task RunCheck3Async()
{
    ScriptedChatClient canonicalClient = new(ChatScripts.Canonical());
    OwnLoopAgent canonical = new(canonicalClient, SpikeTool.All, SystemPrompt, (_, _) => ValueTask.FromResult(ApprovalDecision.AllowOnce));
    PrintTranscript("canonical", await CollectAsync(canonical.RunAsync(ChatScripts.SystemUserMessage)));

    OwnLoopAgent steering = null!;
    ScriptedChatClient steeringClient = new(
    [
        new(
        [
            Updates.Text("Reading first."),
            Updates.CallWithSideEffect(
                "call_read_1",
                "read",
                """{"path":"src/Calculator.cs"}""",
                () => steering.Steer("Also check the tests after the fix.")),
            Updates.Finish(ChatFinishReason.ToolCalls),
        ]),
        new(
        [
            Updates.Text("Noted; I will also check the tests."),
            Updates.Finish(),
        ]),
    ]);
    steering = new OwnLoopAgent(steeringClient, SpikeTool.All, SystemPrompt, (_, _) => ValueTask.FromResult(ApprovalDecision.AllowOnce));
    PrintTranscript("steering", await CollectAsync(steering.RunAsync(ChatScripts.SystemUserMessage)));

    bool steeringInjected = steeringClient.Requests.Count > 1
        && steeringClient.Requests[1].Messages.Any(m => m.Role == ChatRole.User && m.Text.Contains("check the tests", StringComparison.Ordinal));
    Console.WriteLine($"steering_seen_in_next_request={steeringInjected.ToString().ToLowerInvariant()}");

    using CancellationTokenSource cts = new();
    ScriptedChatClient cancelClient = new(
    [
        new(
        [
            Updates.Text("Reading "),
            Updates.Call("call_read_1", "read", """{"path":"src/Calculator.cs"}"""),
            new ScriptedUpdate(new ChatResponseUpdate(ChatRole.Assistant, "and then..."), cts.Cancel),
            Updates.Finish(ChatFinishReason.ToolCalls),
        ]),
    ]);
    OwnLoopAgent cancel = new(cancelClient, SpikeTool.All, SystemPrompt, (_, _) => ValueTask.FromResult(ApprovalDecision.AllowOnce));
    PrintTranscript("cancel", await CollectAsync(cancel.RunAsync(ChatScripts.SystemUserMessage, cts.Token)));
}

static async Task RunCheck4Async()
{
    List<string> callbacks = [];
    ScriptedChatClient client = new(
    [
        new(
        [
            Updates.Text("Building."),
            Updates.Call("c1", "bash", """{"command":"dotnet build"}"""),
            Updates.Finish(ChatFinishReason.ToolCalls),
        ]),
        new(
        [
            Updates.Text("Testing."),
            Updates.Call("c2", "bash", """{"command":"dotnet test"}"""),
            Updates.Finish(ChatFinishReason.ToolCalls),
        ]),
        new(
        [
            Updates.Text("Done."),
            Updates.Finish(),
        ]),
    ]);

    OwnLoopAgent agent = new(
        client,
        SpikeTool.All,
        SystemPrompt,
        (context, _) =>
        {
            callbacks.Add($"name={context.Tool.Name} args={context.ArgsJson}");
            return ValueTask.FromResult(ApprovalDecision.AllowForSession);
        });

    PrintTranscript("approval-session", await CollectAsync(agent.RunAsync(ChatScripts.SystemUserMessage)));
    Console.WriteLine($"approval_callback_invocations={callbacks.Count}");
    foreach (string callback in callbacks)
    {
        Console.WriteLine($"approval_callback {callback}");
    }

    List<string> argumentCallbacks = [];
    ScriptedChatClient argumentClient = new(
    [
        new(
        [
            Updates.Text("Running both."),
            Updates.Call("c1", "bash", """{"command":"rm -rf bin"}"""),
            Updates.Call("c2", "bash", """{"command":"dotnet build"}"""),
            Updates.Finish(ChatFinishReason.ToolCalls),
        ]),
        new(
        [
            Updates.Text("I skipped the destructive one."),
            Updates.Finish(),
        ]),
    ]);

    OwnLoopAgent argumentAgent = new(
        argumentClient,
        SpikeTool.All,
        SystemPrompt,
        (context, _) =>
        {
            argumentCallbacks.Add(context.ArgsJson);
            bool destructive = context.ArgsJson.Contains("rm -rf", StringComparison.Ordinal);
            return ValueTask.FromResult(destructive ? ApprovalDecision.Deny : ApprovalDecision.AllowOnce);
        });

    PrintTranscript("approval-per-arguments", await CollectAsync(argumentAgent.RunAsync(ChatScripts.SystemUserMessage)));
    Console.WriteLine($"argument_callback_invocations={argumentCallbacks.Count}");
}

static async Task RunCheck5Async()
{
    string path = Path.Combine(AppContext.BaseDirectory, "recorded-canonical.jsonl");

    string first = await RunTranscriptAsync(new RecordingChatClient(new ScriptedChatClient(ChatScripts.Canonical()), path));
    string second = await RunTranscriptAsync(new ReplayChatClient(path));

    Console.WriteLine($"recording_file={path}");
    Console.WriteLine($"transcript_recorded_sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first)))[..16]}");
    Console.WriteLine($"transcript_replayed_sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(second)))[..16]}");
    Console.WriteLine($"replay_deterministic={(first == second).ToString().ToLowerInvariant()}");

    if (first != second)
    {
        Console.WriteLine("--- recorded ---");
        Console.WriteLine(first);
        Console.WriteLine("--- replayed ---");
        Console.WriteLine(second);
    }
}

static async Task RunSplitAsync()
{
    ScriptedChatClient client = new(ChatScripts.SplitArguments());
    OwnLoopAgent agent = new(client, SpikeTool.All, SystemPrompt, (_, _) => ValueTask.FromResult(ApprovalDecision.AllowOnce));
    IReadOnlyList<OwnLoopEvent> events = await CollectAsync(agent.RunAsync(ChatScripts.SystemUserMessage));
    PrintTranscript("split-arguments", events);

    bool merged = events.OfType<ToolCallResult>().Any(e => e.Output.Contains("src/Calculator.cs", StringComparison.Ordinal));
    Console.WriteLine($"split_arguments_assembled={merged.ToString().ToLowerInvariant()}");
}

static async Task<string> RunTranscriptAsync(IChatClient client)
{
    OwnLoopAgent agent = new(client, SpikeTool.All, SystemPrompt, (_, _) => ValueTask.FromResult(ApprovalDecision.AllowForSession));
    StringBuilder transcript = new();
    await foreach (OwnLoopEvent item in agent.RunAsync(ChatScripts.SystemUserMessage))
    {
        transcript.AppendLine(item.Line);
    }

    return transcript.ToString();
}

static void PrintTranscript(string name, IReadOnlyList<OwnLoopEvent> events)
{
    Console.WriteLine($"--- transcript {name} ---");
    foreach (OwnLoopEvent item in events)
    {
        Console.WriteLine(item.Line);
    }

    Console.WriteLine($"--- end {name} ---");
}

static async Task<List<OwnLoopEvent>> CollectAsync(IAsyncEnumerable<OwnLoopEvent> events)
{
    List<OwnLoopEvent> list = [];
    await foreach (OwnLoopEvent item in events)
    {
        list.Add(item);
    }

    return list;
}
