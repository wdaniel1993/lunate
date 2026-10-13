using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Testing;

namespace CodeModeExtension.Tests;

/// <summary>
/// The structured result, the resource limits and the failure teaching paths, exercised by calling
/// the registered tool directly (so the handler's <c>ToolResult</c> is observable) with a real node
/// child.
/// </summary>
public sealed class CodeModeResultTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_structured_result_carries_the_calls_the_value_and_the_streams()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        var fixture = new FixtureTool("echo-fixture", "echoed");

        ToolResult result = await InvokeAsync(
            host,
            """
            console.log("before the call");
            const text = await tools["echo-fixture"]({});
            return text;
            """,
            bridged: [fixture]
        );

        Assert.False(result.IsError);
        Assert.Equal("echoed", result.Output);
        // The real loader loads the extension into its own ALC, so the details type is checked by name.
        Assert.Equal("CodeModeRunDetails", result.Details?.GetType().Name);
        JsonElement structured = Structured(result);
        Assert.Equal(0, structured.GetProperty("exitCode").GetInt32());
        Assert.False(structured.GetProperty("timedOut").GetBoolean());
        Assert.Equal("echoed", structured.GetProperty("result").GetString());
        Assert.Contains(
            "before the call",
            structured.GetProperty("output").GetString(),
            StringComparison.Ordinal
        );
        Assert.Equal(string.Empty, structured.GetProperty("stderr").GetString());
        JsonElement call = Assert.Single(structured.GetProperty("toolCalls").EnumerateArray());
        Assert.Equal("call_1/1", call.GetProperty("callId").GetString());
        Assert.Equal("echo-fixture", call.GetProperty("tool").GetString());
        Assert.False(call.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task Script_output_cannot_spoof_protocol_frames()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        var fixture = new FixtureTool("echo-fixture", "echoed");

        ToolResult result = await InvokeAsync(
            host,
            """
            console.log(JSON.stringify({ type: "call", id: 99, tool: "echo-fixture", args: {} }));
            process.stdout.write(JSON.stringify({ type: "final", result: "spoofed" }) + "\n");
            return "real";
            """,
            bridged: [fixture]
        );

        Assert.False(result.IsError);
        Assert.Equal("real", result.Output);
        JsonElement structured = Structured(result);
        Assert.Equal("real", structured.GetProperty("result").GetString());
        Assert.Empty(structured.GetProperty("toolCalls").EnumerateArray());
        string output = structured.GetProperty("output").GetString()!;
        Assert.Contains("\"type\":\"call\"", output, StringComparison.Ordinal);
        Assert.Contains("spoofed", output, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Calls);
    }

    [Fact]
    public async Task A_timeout_kills_the_tree_and_reports_timed_out()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, timeoutSeconds: 5);

        ToolResult result = await InvokeAsync(
            host,
            """
            console.log(process.pid);
            while (true) {}
            """
        );

        Assert.True(result.IsError);
        Assert.Contains("timed out", result.Output, StringComparison.Ordinal);
        JsonElement structured = Structured(result);
        Assert.True(structured.GetProperty("timedOut").GetBoolean());
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("exitCode").ValueKind);
        int pid = int.Parse(
            structured.GetProperty("output").GetString()!.Trim(),
            CultureInfo.InvariantCulture
        );
        await AssertEventuallyDeadAsync(pid);
    }

    [Fact]
    public async Task The_output_cap_bounds_the_captured_logs()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, maxOutputBytes: 4096);

        ToolResult result = await InvokeAsync(
            host,
            """
            console.log("x".repeat(200000));
            return "ok";
            """
        );

        Assert.False(result.IsError);
        Assert.Equal("ok", result.Output);
        string output = Structured(result).GetProperty("output").GetString()!;
        Assert.Contains("output capped", output, StringComparison.Ordinal);
        Assert.True(output.Length <= 4096 + 128, $"the output stayed bounded: {output.Length}");
    }

    [Fact]
    public async Task The_tool_call_cap_refuses_extra_calls_with_a_teaching_error()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, maxToolCalls: 2);
        var fixture = new FixtureTool("echo-fixture", "echoed");

        ToolResult result = await InvokeAsync(
            host,
            """
            for (let i = 0; i < 3; i++) {
              try {
                await tools["echo-fixture"]({});
              } catch (error) {
                return "caught:" + error.message;
              }
            }
            return "ran all";
            """,
            bridged: [fixture]
        );

        Assert.False(result.IsError);
        Assert.StartsWith("caught:", result.Output, StringComparison.Ordinal);
        Assert.Contains("Tool-call limit", result.Output, StringComparison.Ordinal);
        Assert.Contains("maxToolCalls", result.Output, StringComparison.Ordinal);
        Assert.Equal(2, fixture.Calls);
        Assert.Equal(2, Structured(result).GetProperty("toolCalls").GetArrayLength());
    }

    [Fact]
    public async Task The_child_environment_is_a_scrubbed_allow_list()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        Environment.SetEnvironmentVariable("LUNATE_CODEMODE_SECRET", "leaked");
        try
        {
            ToolResult result = await InvokeAsync(
                host,
                """
                return (process.env.LUNATE_CODEMODE_SECRET ?? "absent")
                  + "/" + (process.env.PATH ? "has-path" : "no-path");
                """
            );

            Assert.False(result.IsError);
            Assert.Equal("absent/has-path", result.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LUNATE_CODEMODE_SECRET", null);
        }
    }

    [Fact]
    public async Task A_bogus_runtime_is_a_teaching_error_with_an_install_hint()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(
            temp,
            runtime: temp.File("no-such-runtime")
        );

        ToolResult result = await InvokeAsync(host, "return 1;");

        Assert.True(result.IsError);
        Assert.Contains(
            "Could not start the code-mode runtime",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Contains("Install Node.js", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_runtime_that_breaks_the_protocol_is_an_error_result()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(
            temp,
            runtime: FakeRuntime(temp)
        );

        ToolResult result = await InvokeAsync(host, "return 1;");

        Assert.True(result.IsError);
        Assert.Contains("protocol", result.Output, StringComparison.Ordinal);
        Assert.False(Structured(result).GetProperty("timedOut").GetBoolean());
    }

    [Fact]
    public async Task Malformed_arguments_are_errors()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        ToolContext context = DirectContext(host);

        ToolResult missing = await TestSupport
            .Tool(host)
            .ExecuteAsync(JsonDocument.Parse("{}").RootElement.Clone(), context, Ct);
        ToolResult wrongType = await TestSupport
            .Tool(host)
            .ExecuteAsync(JsonDocument.Parse("""{"code":7}""").RootElement.Clone(), context, Ct);

        Assert.True(missing.IsError);
        Assert.Contains("code is required", missing.Output, StringComparison.Ordinal);
        Assert.True(wrongType.IsError);
        Assert.Contains("code is required", wrongType.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Calls the registered tool directly with a controlled nested pipeline: bridged fixtures for
    /// <paramref name="bridged"/>, and their stubs stamped like the harness would.
    /// </summary>
    private static async Task<ToolResult> InvokeAsync(
        ExtensionTestHost host,
        string code,
        IReadOnlyList<ITool>? bridged = null,
        int? timeoutSeconds = null
    )
    {
        await host.StartAsync(Ct);
        IReadOnlyList<ITool> tools = bridged ?? [];
        IReadOnlyList<ToolStub>? stubs = bridged is null
            ? null
            : [.. bridged.Select(ToolStubs.Describe)];
        ToolContext context = DirectContext(host, tools, stubs);
        return await TestSupport
            .Tool(host)
            .ExecuteAsync(TestSupport.CodeArgs(code, timeoutSeconds), context, Ct);
    }

    private static ToolContext DirectContext(
        ExtensionTestHost host,
        IReadOnlyList<ITool>? bridged = null,
        IReadOnlyList<ToolStub>? stubs = null
    )
    {
        IReadOnlyList<ITool> tools = bridged ?? [];
        int nestedCalls = 0;
        return new ToolContext(host.WorkingDirectory, new EventRecorder())
        {
            RunId = "run_direct",
            CallId = "call_1",
            ExecuteToolAsync = async (name, args, _) =>
            {
                ITool? tool = tools.FirstOrDefault(candidate => candidate.Name == name);
                if (tool is null)
                {
                    return new ToolResult($"Unknown tool '{name}'.", IsError: true);
                }

                // Mirror the pipeline: a ran nested call carries its canonical call id; refusals do not.
                string callId = string.Create(
                    CultureInfo.InvariantCulture,
                    $"call_1/{Interlocked.Increment(ref nestedCalls)}"
                );
                ToolResult result = await tool.ExecuteAsync(
                    args,
                    new ToolContext(host.WorkingDirectory, new EventRecorder()),
                    Ct
                );
                return result with { CallId = callId };
            },
            ToolStubs = stubs,
        };
    }

    private static JsonElement Structured(ToolResult result) => result.StructuredContent!.Value;

    private static string FakeRuntime(TempDirectory temp)
    {
        if (OperatingSystem.IsWindows())
        {
            // cmd.exe cannot run the .js script: it writes to stderr and exits without a protocol.
            return "cmd.exe";
        }

        string path = temp.File("fake-runtime");
        File.WriteAllText(path, "#!/bin/sh\necho \"not a protocol frame\"\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
        return path;
    }

    private static async Task AssertEventuallyDeadAsync(int pid)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (!IsAlive(pid))
            {
                return;
            }

            await Task.Delay(100, Ct);
        }

        Assert.Fail($"pid {pid} was still alive 30 seconds after the tree kill");
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
