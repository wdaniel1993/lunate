using System.ComponentModel;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The ripgrep-backed grep engine: <c>--json</c> events parsed for path, line and text, with
/// <c>--sort path</c> so the shape matches the fallback. Used only when <c>rg</c> already resolves
/// on PATH; its absence is not an error.
/// </summary>
internal sealed class RipgrepGrepEngine : IGrepEngine
{
    private const int OutputCapBytes = 16 * 1024 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static readonly ProcessRunner Runner = new();

    private readonly string _executable;
    private readonly ITextFileAccess _files;

    internal RipgrepGrepEngine(string executable, ITextFileAccess? files = null)
    {
        _executable = executable;
        _files = files ?? LocalTextFileAccess.Instance;
    }

    /// <summary>The engine when <c>rg</c> resolves on PATH, else null (the fallback takes over).</summary>
    internal static IGrepEngine? TryCreate() =>
        ShellProbe.Current.FindOnPath("rg") is { } executable
            ? new RipgrepGrepEngine(executable)
            : null;

    public async Task<GrepSearch> SearchAsync(GrepRequest request, CancellationToken ct)
    {
        List<string> arguments =
        [
            "--json",
            "--color",
            "never",
            "--sort",
            "path",
            "--hidden",
            "--no-require-git",
            "--glob",
            "!.git",
            "--glob",
            "!.git/**",
            request.CaseSensitive ? "--case-sensitive" : "--ignore-case",
        ];
        if (!OperatingSystem.IsLinux())
        {
            // The glob rule follows the workspace: insensitive on Windows and macOS.
            arguments.Add("--glob-case-insensitive");
        }

        if (request.Glob is { } glob)
        {
            arguments.Add("--glob");
            arguments.Add(glob);
        }

        arguments.Add("--");
        arguments.Add(request.Pattern);
        // Always pass a path: with no path (and a non-TTY stdin) ripgrep searches stdin instead
        // of the working directory, which is closed here.
        arguments.Add(request.RelativeRoot is "." or "" ? "." : request.RelativeRoot);

        ProcessRunResult run;
        try
        {
            run = await Runner.RunAsync(
                new ProcessRunRequest(
                    _executable,
                    arguments,
                    request.Root,
                    Timeout: Timeout,
                    OutputCapBytes: OutputCapBytes
                ),
                ct
            );
        }
        catch (Exception exception)
            when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            return Error($"could not run ripgrep: {exception.Message}");
        }

        if (run.TimedOut)
        {
            return Error(
                $"search timed out after {Timeout.TotalSeconds:0} s; narrow the path or glob"
            );
        }

        if (run.OutputCapped)
        {
            return Error("search output exceeded the 16 MB cap; narrow the path, glob or pattern");
        }

        if (run.ExitCode == 1 && string.IsNullOrWhiteSpace(run.Stderr))
        {
            return new GrepSearch([], 0, Error: null);
        }

        if (run.ExitCode != 0)
        {
            string detail = run.Stderr.Trim();
            return Error(
                detail.Contains("regex parse error", StringComparison.OrdinalIgnoreCase)
                    ? $"pattern is not a valid regular expression: {detail}"
                    : $"ripgrep failed: {detail}"
            );
        }

        return Parse(run.Stdout, request);
    }

    private GrepSearch Parse(string output, GrepRequest request)
    {
        List<GrepMatch> matches = [];
        Dictionary<string, bool> binary = new(StringComparer.Ordinal);
        var more = 0;
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (
                    root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("type", out JsonElement type)
                    || type.GetString() != "match"
                    || !root.TryGetProperty("data", out JsonElement data)
                )
                {
                    continue;
                }

                string path = data.GetProperty("path")
                    .GetProperty("text")
                    .GetString()!
                    .Replace('\\', '/');
                if (path.StartsWith("./", StringComparison.Ordinal))
                {
                    path = path[2..];
                }
                if (!binary.TryGetValue(path, out bool isBinary))
                {
                    isBinary = ProbeBinary(path, request.Root);
                    binary[path] = isBinary;
                }

                if (isBinary)
                {
                    continue;
                }

                int number = data.GetProperty("line_number").GetInt32();
                string text =
                    data.GetProperty("lines").GetProperty("text").GetString() ?? string.Empty;
                text = text.TrimEnd('\n');
                if (text.EndsWith('\r'))
                {
                    text = text[..^1];
                }

                if (matches.Count < request.MaxResults)
                {
                    matches.Add(new GrepMatch(path, number, text));
                }
                else
                {
                    more++;
                }
            }
        }

        return new GrepSearch(matches, more, Error: null);
    }

    private bool ProbeBinary(string path, string root)
    {
        try
        {
            string absolute = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            return _files.ReadPrefix(absolute, 8192) is { } probe && probe.Text.Contains('\0');
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    private static GrepSearch Error(string message) => new([], 0, message);
}
