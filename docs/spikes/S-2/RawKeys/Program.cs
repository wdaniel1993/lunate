using Spike.RawKeys;

if (args is ["--selftest"])
    return SelfTest.Run();
if (args is ["--probe"])
    return Probe.Run();
if (args is ["--headless"])
    return Headless.Run(Console.OpenStandardInput());
if (args.Length == 0)
{
    return Console.IsInputRedirected
        ? Headless.Run(Console.OpenStandardInput())
        : Interactive.Run();
}

Console.Error.WriteLine("usage: RawKeys [--selftest|--probe|--headless]");
return 2;
