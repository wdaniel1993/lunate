namespace Spike.WorkspaceProbe;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: WorkspaceProbe <selftest|locate|load> [args]");
            return 2;
        }

        return args[0] switch
        {
            "selftest" => SelfTest.Run(),
            "locate" => Locate.Run(),
            "load" => await Load.RunAsync(args[1..]).ConfigureAwait(false),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"unknown command: {command}");
        return 2;
    }
}
