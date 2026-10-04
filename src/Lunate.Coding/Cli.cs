using System.Reflection;

namespace Lunate.Coding;

public static class Cli
{
    public static int Run(string[] args, TextWriter output)
    {
        if (args is ["--version"])
        {
            output.WriteLine(GetProductVersion());
        }

        return 0;
    }

    private static string GetProductVersion()
    {
        var assembly = typeof(Cli).Assembly;

        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
