using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spike.Shared;

namespace Spike.MafHarness;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "all";

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
                await ApprovalChecks.RunAllAsync();
                break;
            case "check5":
                await RunCheck5Async();
                break;
            case "split":
                await RunSplitAsync();
                break;
            case "errorpath":
                await RunErrorPathAsync();
                break;
            default:
                Console.Error.WriteLine($"Unknown mode '{mode}'.");
                return 2;
        }

        return 0;
    }

    private static async Task ProbeAsync()
    {
        DateTime processStart = Process.GetCurrentProcess().StartTime;
        Stopwatch buildWatch = Stopwatch.StartNew();
        ScriptedChatClient client = new([new([Updates.Text("ready")])]);
        AIAgent agent = HarnessFactory.Create(client);
        buildWatch.Stop();

        Console.WriteLine($"agent_build_ms={buildWatch.Elapsed.TotalMilliseconds:F1}");
        Console.WriteLine($"startup_ms={(DateTime.Now - processStart).TotalMilliseconds:F1}");

        Stopwatch firstRunWatch = Stopwatch.StartNew();
        _ = await HarnessRunner.CollectUpdatesAsync(agent, "hello", session: null);
        firstRunWatch.Stop();
        Console.WriteLine($"first_run_ms={firstRunWatch.Elapsed.TotalMilliseconds:F1}");

        Console.WriteLine($"managed_heap_bytes={GC.GetTotalMemory(forceFullCollection: true)}");
        Console.WriteLine($"pid={Environment.ProcessId}");
        Console.WriteLine("READY");
        await Task.Delay(TimeSpan.FromSeconds(8));
    }

    private static void RunCheck2()
    {
        ScriptedChatClient emptyHarnessClient = new([new([Updates.Text("ok")])]);
        AIAgent emptyHarness = HarnessFactory.Create(emptyHarnessClient);
        _ = HarnessRunner.CollectUpdatesAsync(emptyHarness, "hello", session: null).GetAwaiter().GetResult();

        Console.WriteLine("--- prompt surface (harness, harness instructions omitted) ---");
        foreach (string line in PromptSurface.Measure(emptyHarnessClient.Requests[0]).Lines())
        {
            Console.WriteLine(line);
        }

        ScriptedChatClient defaultInstructionsClient = new([new([Updates.Text("ok")])]);
        AIAgent defaultInstructions = HarnessFactory.Create(defaultInstructionsClient, defaultHarnessInstructions: true);
        _ = HarnessRunner.CollectUpdatesAsync(defaultInstructions, "hello", session: null).GetAwaiter().GetResult();

        Console.WriteLine("--- prompt surface (harness, default harness instructions) ---");
        foreach (string line in PromptSurface.Measure(defaultInstructionsClient.Requests[0]).Lines())
        {
            Console.WriteLine(line);
        }

        Console.WriteLine($"harness_default_instructions_chars={HarnessAgent.DefaultInstructions.Length}");
    }

    private static async Task RunCheck3Async()
    {
        ScriptedChatClient canonicalClient = new(ChatScripts.Canonical());
        AIAgent canonical = HarnessFactory.Create(canonicalClient);
        List<AgentResponseUpdate> canonicalUpdates = await HarnessRunner.CollectUpdatesAsync(canonical, ChatScripts.SystemUserMessage, session: null);
        Console.WriteLine("--- transcript canonical (harness) ---");
        HarnessRunner.PrintEvents(HarnessRunner.MapUpdates(canonicalUpdates));
        Console.WriteLine("--- end canonical (harness) ---");

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
        AIAgent cancelAgent = HarnessFactory.Create(cancelClient);
        List<string> diagnostics = [];
        List<AgentResponseUpdate> cancelUpdates = await HarnessRunner.CollectUpdatesAsync(cancelAgent, ChatScripts.SystemUserMessage, session: null, diagnostics, cts.Token);
        Console.WriteLine("--- transcript cancel (harness) ---");
        HarnessRunner.PrintEvents(HarnessRunner.MapUpdates(cancelUpdates));
        foreach (string line in diagnostics)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine("--- end cancel (harness) ---");
        Console.WriteLine("steering_built_in=false (AIAgent exposes no steer/inject API; the app must queue and replay messages itself)");
    }

    private static async Task RunCheck5Async()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "recorded-maf.jsonl");

        string first = await RunTranscriptAsync(new RecordingChatClient(new ScriptedChatClient(ChatScripts.Canonical()), path));
        string second = await RunTranscriptAsync(new ReplayChatClient(path));

        Console.WriteLine($"recording_file={path}");
        Console.WriteLine($"transcript_recorded_sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first)))[..16]}");
        Console.WriteLine($"transcript_replayed_sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(second)))[..16]}");
        Console.WriteLine($"replay_deterministic={(first == second).ToString().ToLowerInvariant()}");

        ScriptedChatClient sessionClient = new(ChatScripts.Canonical());
        AIAgent sessionAgent = HarnessFactory.Create(sessionClient);
        AgentSession session = await sessionAgent.CreateSessionAsync();
        _ = await HarnessRunner.CollectUpdatesAsync(sessionAgent, ChatScripts.SystemUserMessage, session);

        JsonElement serialized = await sessionAgent.SerializeSessionAsync(session);
        Console.WriteLine($"session_serialized_chars={serialized.GetRawText().Length}");

        try
        {
            _ = await sessionAgent.DeserializeSessionAsync(serialized);
            Console.WriteLine("session_roundtrip=true");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"session_roundtrip=false error={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task RunSplitAsync()
    {
        ScriptedChatClient client = new(ChatScripts.SplitArguments());
        AIAgent agent = HarnessFactory.Create(client);
        List<AgentResponseUpdate> updates = await HarnessRunner.CollectUpdatesAsync(agent, ChatScripts.SystemUserMessage, session: null);
        Console.WriteLine("--- transcript split-arguments (harness) ---");
        HarnessRunner.PrintEvents(HarnessRunner.MapUpdates(updates));
        Console.WriteLine("--- end split-arguments (harness) ---");
        Console.WriteLine($"tool_invocations={updates.SelectMany(u => u.Contents).OfType<FunctionResultContent>().Count()}");
    }

    private static async Task RunErrorPathAsync()
    {
        // Revision item 2: a tool that RETURNS an error result (no exception)
        // vs a tool that THROWS, through the harness's FIC pipeline.
        SpikeTool readError = new(
            "read",
            SpikeTool.Read.Description,
            SpikeTool.Read.ParametersSchemaJson,
            SpikeToolRisk.ReadOnly,
            args => $"Error: file not found at {args.GetProperty("path").GetString()}. Use bash ls to locate the file.");

        SpikeTool bashThrow = new(
            "bash",
            SpikeTool.Bash.Description,
            SpikeTool.Bash.ParametersSchemaJson,
            SpikeToolRisk.Execute,
            _ => throw new InvalidOperationException("bash: command timed out after 120s; consider raising timeout_s or splitting the command."));

        ScriptedChatClient client = new(
        [
            new(
            [
                Updates.Text("Reading the file."),
                Updates.Call("call_read_err", "read", """{"path":"src/Missing.cs"}"""),
                Updates.Finish(ChatFinishReason.ToolCalls),
            ]),
            new(
            [
                Updates.Text("Running the command."),
                Updates.Call("call_bash_err", "bash", """{"command":"sleep 999"}"""),
                Updates.Finish(ChatFinishReason.ToolCalls),
            ]),
            new(
            [
                Updates.Text("Done."),
                Updates.Finish(),
            ]),
        ]);

        AIAgent agent = HarnessFactory.Create(client, toolsOverride: [readError, bashThrow]);
        _ = await HarnessRunner.CollectUpdatesAsync(agent, ChatScripts.SystemUserMessage, session: null);

        Console.WriteLine("--- error-path probe (harness) ---");
        foreach (FunctionResultContent result in client.Requests
                     .SelectMany(r => r.Messages)
                     .SelectMany(m => m.Contents)
                     .OfType<FunctionResultContent>())
        {
            string text = result.Result?.ToString() ?? "(null)";
            string exception = result.Exception is null ? "none" : $"{result.Exception.GetType().Name}: {result.Exception.Message}";
            Console.WriteLine($"call={result.CallId} result=\"{text}\" exception={exception}");
        }

        Console.WriteLine("--- end error-path probe (harness) ---");
    }

    private static async Task<string> RunTranscriptAsync(IChatClient client)
    {
        AIAgent agent = HarnessFactory.Create(client);
        List<AgentResponseUpdate> updates = await HarnessRunner.CollectUpdatesAsync(agent, ChatScripts.SystemUserMessage, session: null);
        StringBuilder transcript = new();
        foreach (MappedEvent item in HarnessRunner.MapUpdates(updates))
        {
            transcript.AppendLine(item.Line);
        }

        return transcript.ToString();
    }
}
