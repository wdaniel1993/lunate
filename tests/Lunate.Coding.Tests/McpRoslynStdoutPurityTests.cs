using System.Diagnostics;
using System.Text.Json;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>lunate mcp roslyn</c> contract on the built binary: initialize and tools/list answer, and
/// only protocol frames reach stdout — the startup diagnostic goes to stderr. Runs in a directory
/// with no solution, where the server still starts and lists every tool.
/// </summary>
public sealed class McpRoslynStdoutPurityTests
{
    [Fact]
    public async Task Mcp_roslyn_answers_initialize_and_tools_list_with_pure_stdout()
    {
        string binary = Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "lunate.exe" : "lunate"
        );
        Assert.SkipUnless(
            File.Exists(binary),
            $"the built lunate binary was not found at {binary}; run a full build first"
        );

        using var temp = new TempDirectory();
        var startInfo = new ProcessStartInfo(binary)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = temp.Root,
        };
        startInfo.ArgumentList.Add("mcp");
        startInfo.ArgumentList.Add("roslyn");
        // No solution here, and no global state the server could read.
        startInfo.Environment["HOME"] = temp.Root;
        startInfo.Environment["USERPROFILE"] = temp.Root;

        using Process process = Process.Start(startInfo)!;
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
            StreamWriter stdin = process.StandardInput;

            await stdin.WriteLineAsync(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","clientInfo":{"name":"purity-test","version":"0.0.1"},"capabilities":{}}}"""
            );
            await stdin.WriteLineAsync(
                """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}"""
            );
            await stdin.WriteLineAsync(
                """{"jsonrpc":"2.0","id":3,"method":"lunate/bogus","params":{}}"""
            );
            await stdin.FlushAsync(ct);

            var responses = new Dictionary<int, JsonElement>();
            while (responses.Count < 3)
            {
                string line = await ReadLineAsync(process.StandardOutput, ct);
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement frame = document.RootElement.Clone();
                Assert.Equal("2.0", frame.GetProperty("jsonrpc").GetString());
                Assert.True(
                    frame.TryGetProperty("result", out _) || frame.TryGetProperty("error", out _),
                    $"stdout line is not a JSON-RPC response frame: {line}"
                );
                responses[frame.GetProperty("id").GetInt32()] = frame;
            }

            Assert.Equal(
                "lunate-roslyn",
                responses[1]
                    .GetProperty("result")
                    .GetProperty("serverInfo")
                    .GetProperty("name")
                    .GetString()
            );
            string[] tools =
            [
                .. responses[2]
                    .GetProperty("result")
                    .GetProperty("tools")
                    .EnumerateArray()
                    .Select(tool => tool.GetProperty("name").GetString()!)
                    .Order(StringComparer.Ordinal),
            ];
            Assert.Equal(
                [
                    "cs_diagnostics",
                    "cs_find_references",
                    "cs_find_symbol",
                    "cs_outline",
                    "cs_rename",
                ],
                tools
            );
            Assert.Equal(-32601, responses[3].GetProperty("error").GetProperty("code").GetInt32());

            stdin.Close();
            Assert.True(
                process.WaitForExit((int)WaitBudgets.ProcessExit.TotalMilliseconds),
                "lunate mcp roslyn did not exit after stdin closed"
            );
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("", await process.StandardOutput.ReadToEndAsync(ct));
            Assert.Contains("lunate mcp roslyn:", await stderr, StringComparison.Ordinal);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task<string> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        string? line = await reader
            .ReadLineAsync(ct)
            .AsTask()
            .WaitAsync(WaitBudgets.SpawnWindow, ct);
        Assert.NotNull(line);
        return line;
    }
}
