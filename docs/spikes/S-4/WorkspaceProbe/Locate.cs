using Microsoft.Build.Locator;

namespace Spike.WorkspaceProbe;

internal static class Locate
{
    public static int Run()
    {
        var instances = MSBuildLocator
            .QueryVisualStudioInstances()
            .OrderByDescending(instance => instance.Version)
            .ToArray();
        Console.WriteLine($"locator_instances: {instances.Length}");
        foreach (var instance in instances)
        {
            Console.WriteLine(
                $"locator_instance: name={instance.Name} version={instance.Version} "
                    + $"discovery={instance.DiscoveryType} msbuild_path={instance.MSBuildPath}"
            );
        }

        if (!MSBuildLocator.IsRegistered)
        {
            var registered = MSBuildLocator.RegisterDefaults();
            Console.WriteLine(
                $"locator_registered: name={registered.Name} version={registered.Version} "
                    + $"discovery={registered.DiscoveryType}"
            );
        }
        else
        {
            Console.WriteLine("locator_registered: already");
        }

        Console.WriteLine(
            $"msbuild_exe_path: {Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH") ?? "<unset>"}"
        );
        Console.WriteLine(
            $"msbuild_sdk: {Environment.GetEnvironmentVariable("MSBuildSDKsPath") ?? "<unset>"}"
        );
        return 0;
    }
}
