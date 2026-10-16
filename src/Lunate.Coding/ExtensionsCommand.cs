using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>
/// The <c>lunate extensions [--json]</c> subcommand (design D3): it lists every discovered
/// extension with its scope, state, CLI-measured load time and contribution counts, composing the
/// host in register-only inspect mode — trusted extensions load and register, nothing starts and
/// nothing prompts, so an untrusted project extension reads <c>trust-denied</c>. After rendering,
/// each loaded extension is unloaded (releasing its collectible context) and the host disposed.
/// Exit codes: 0 success, 1 an extension failed to load, 2 usage; denied is expected without trust
/// and stays 0.
/// </summary>
internal static class ExtensionsCommand
{
    internal const int ExitOk = 0;
    internal const int ExitFailure = 1;
    internal const int ExitUsage = 2;

    internal const string Usage = "Usage: lunate extensions [--json]";

    internal static int Run(
        string[] args,
        string? workingDirectory,
        TextWriter output,
        TextWriter errors,
        ExtensionSeams? seams
    )
    {
        if (!TryParse(args, errors, out bool json))
        {
            return ExitUsage;
        }

        try
        {
            return List(workingDirectory, output, seams, json);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return ExitFailure;
        }
    }

    private static int List(
        string? workingDirectory,
        TextWriter output,
        ExtensionSeams? seams,
        bool json
    ) => ListAsync(workingDirectory, output, seams, json).GetAwaiter().GetResult();

    private static async Task<int> ListAsync(
        string? workingDirectory,
        TextWriter output,
        ExtensionSeams? seams,
        bool json
    )
    {
        ExtensionSeams resolved = (seams ?? new ExtensionSeams()) with { Inspect = true };
        if (resolved.NoExtensions)
        {
            if (json)
            {
                output.Write("[]\n");
            }
            else
            {
                output.WriteLine(ExtensionsView.DisabledText);
            }

            return ExitOk;
        }

        var workspace = new Workspace(workingDirectory ?? Environment.CurrentDirectory);
        await using ExtensionSession host = await ExtensionSession
            .StartAsync(workspace.Identity, resolved)
            .ConfigureAwait(false);
        IReadOnlyList<ExtensionDescriptor> descriptors = host.Descriptors;
        IReadOnlyList<ExtensionLoadResult> results = host.LoadResults;
        try
        {
            if (json)
            {
                output.Write(ExtensionsView.Json(descriptors, results, host.RegisteredCounts));
                output.Write('\n');
            }
            else if (descriptors.Count == 0)
            {
                output.WriteLine(ExtensionsView.NoneText);
            }
            else
            {
                foreach (
                    string segment in ExtensionsView.Segments(
                        descriptors,
                        results,
                        host.RegisteredCounts,
                        cliMeasured: true
                    )
                )
                {
                    output.WriteLine(segment);
                }
            }

            return results.Any(result => result.State == ExtensionLoadState.Failed)
                ? ExitFailure
                : ExitOk;
        }
        finally
        {
            foreach (ExtensionLoadResult result in results)
            {
                await host.UnloadAsync(result.Id, notifyToolsChanged: false).ConfigureAwait(false);
            }
        }
    }

    private static bool TryParse(string[] args, TextWriter errors, out bool json)
    {
        json = false;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--json" when !json:
                    json = true;
                    break;
                default:
                    errors.WriteLine(Usage);
                    return false;
            }
        }

        return true;
    }
}
