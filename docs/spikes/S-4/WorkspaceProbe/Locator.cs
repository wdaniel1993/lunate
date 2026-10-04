using Microsoft.Build.Locator;

namespace Spike.WorkspaceProbe;

internal static class Locator
{
    public static void EnsureRegistered()
    {
        if (MSBuildLocator.IsRegistered)
        {
            return;
        }

        var instance = MSBuildLocator.RegisterDefaults();
        Console.WriteLine($"locator: registered name={instance.Name} version={instance.Version} discovery={instance.DiscoveryType}");
        Console.WriteLine($"locator: msbuild_path={instance.MSBuildPath}");
        Console.WriteLine($"locator: msbuild_exe_path={Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH") ?? "<unset>"}");
    }
}
