using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;

namespace Lunate.Roslyn;

internal readonly record struct BootstrapResult(bool Succeeded, string? FailureMessage);

/// <summary>
/// Registers the MSBuild locator exactly once per process, before any Microsoft.Build type is
/// touched (ADR-0006). The registration method is isolated and never inlined so the assembly and
/// its MSBuild dependency load only on the first call.
/// </summary>
internal static class MsBuildBootstrap
{
    private static readonly object Gate = new();
    private static BootstrapResult? _cached;

    /// <summary>Test seam replacing the real registration; participates in the cache.</summary>
    internal static Func<BootstrapResult>? TestRegisterOverride { get; set; }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _cached = null;
        }
    }

    internal static BootstrapResult EnsureInitialized()
    {
        lock (Gate)
        {
            return _cached ??= (TestRegisterOverride ?? Register)();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BootstrapResult Register()
    {
        // Node reuse would leave MSBuild nodes waiting after our loads (S-4 hygiene); our child
        // processes inherit this. Must be set before the first build-host launch.
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");

        try
        {
            var instance = MSBuildLocator.RegisterDefaults();
            if (instance is null)
            {
                return new BootstrapResult(false, MissingSdkMessage);
            }

            return new BootstrapResult(true, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new BootstrapResult(false, $"{MissingSdkMessage} ({exception.Message})");
        }
    }

    private const string MissingSdkMessage =
        "no .NET SDK could be located; install the .NET SDK or run from a machine with one "
        + "(https://dotnet.microsoft.com/download)";
}
