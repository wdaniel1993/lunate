using System.Diagnostics;

namespace Lunate.Coding.Tests;

/// <summary>
/// Runs one test method in its own process, the pattern <c>MsblBindingOrderTests</c> uses: the
/// process-wide environment cannot be mutated under the tests running beside this one, and a child
/// that sets <c>HOME</c> to a directory that does not exist proves nothing writes there.
/// <para>
/// The parent calls the same method; <see cref="IsChild"/> tells the two halves apart. The marker is
/// the method name, so a child spawned for one proof can never run another's body.
/// </para>
/// </summary>
internal static class TestChildProcess
{
    private const string MarkerVariable = "LUNATE_TEST_CHILD_METHOD";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    internal static bool IsChild(string method) =>
        string.Equals(
            Environment.GetEnvironmentVariable(MarkerVariable),
            method,
            StringComparison.Ordinal
        );

    /// <summary>
    /// Spawns <paramref name="method"/> of <paramref name="testClass"/> in a fresh process. A null
    /// value in <paramref name="environment"/> removes the variable from the child.
    /// </summary>
    internal static async Task<(int ExitCode, string Output)> RunAsync(
        Type testClass,
        string method,
        IReadOnlyDictionary<string, string?>? environment = null
    )
    {
        string assembly = testClass.Assembly.Location;
        string? processPath = Environment.ProcessPath;
        bool direct =
            processPath is not null
            && string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                Path.GetFileNameWithoutExtension(assembly),
                StringComparison.Ordinal
            );

        var start = new ProcessStartInfo(direct ? processPath! : DotNetPath())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        };
        if (!direct)
        {
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add(assembly);
        }

        start.ArgumentList.Add("-method");
        start.ArgumentList.Add(string.Concat(testClass.FullName, ".", method));
        start.ArgumentList.Add("-noLogo");
        start.Environment[MarkerVariable] = method;
        if (environment is not null)
        {
            foreach ((string name, string? value) in environment)
            {
                if (value is null)
                {
                    start.Environment.Remove(name);
                }
                else
                {
                    start.Environment[name] = value;
                }
            }
        }

        using var child = Process.Start(start)!;
        Task<string> output = child.StandardOutput.ReadToEndAsync();
        Task<string> error = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            await child.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                child.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }

            throw new TimeoutException(
                $"{testClass.Name}.{method} did not finish within {Timeout.TotalMinutes:0} minutes."
            );
        }

        return (child.ExitCode, string.Concat(await output, await error));
    }

    private static string DotNetPath()
    {
        string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return !string.IsNullOrEmpty(host) && File.Exists(host) ? host : "dotnet";
    }
}
