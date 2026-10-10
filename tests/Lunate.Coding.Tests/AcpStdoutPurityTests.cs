using System.Diagnostics;
using System.Text.Json;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>--acp</c> contract on the built binary: only protocol frames on stdout, every diagnostic
/// on stderr. Follows the repo's binary-availability skip pattern (the published/built app host may
/// be absent), and never sends a prompt, so no live model is touched.
/// </summary>
public sealed class AcpStdoutPurityTests
{
    [Fact]
    public async Task Acp_mode_keeps_stdout_pure_and_logs_to_stderr()
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
        startInfo.ArgumentList.Add("--acp");
        // Isolate settings, models and sessions: a fresh home directory configures no model, so
        // session/new fails fast and the test provably never reaches a provider.
        startInfo.Environment["HOME"] = temp.Root;
        startInfo.Environment["USERPROFILE"] = temp.Root;
        startInfo.Environment.Remove("LUNATE_MODEL");
        startInfo.Environment.Remove("LUNATE_APPROVAL");

        using Process process = Process.Start(startInfo)!;
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
            StreamWriter stdin = process.StandardInput;

            await stdin.WriteLineAsync(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientInfo":{"name":"purity-test","version":"0.0.1"},"clientCapabilities":{}}}"""
            );
            await stdin.WriteLineAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        jsonrpc = "2.0",
                        id = 2,
                        method = "session/new",
                        @params = new { cwd = temp.Root, mcpServers = Array.Empty<string>() },
                    }
                )
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
                1,
                responses[1].GetProperty("result").GetProperty("protocolVersion").GetInt32()
            );
            Assert.True(
                responses[2].TryGetProperty("result", out _)
                    || responses[2].TryGetProperty("error", out _)
            );
            Assert.Equal(-32601, responses[3].GetProperty("error").GetProperty("code").GetInt32());

            stdin.Close();
            Assert.True(process.WaitForExit(15000), "lunate --acp did not exit after stdin closed");
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("", await process.StandardOutput.ReadToEndAsync(ct));
            Assert.Contains("lunate:", await stderr, StringComparison.Ordinal);
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
            .WaitAsync(TimeSpan.FromSeconds(30), ct);
        Assert.NotNull(line);
        return line;
    }
}
