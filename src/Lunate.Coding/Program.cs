using Lunate.Ai;
using Lunate.Coding;

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler handler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += handler;
try
{
    return Cli.Run(
        args,
        Console.Out,
        Console.Error,
        cancellation.Token,
        hardlinkScanRoot: LunateHome.Store
    );
}
finally
{
    Console.CancelKeyPress -= handler;
}
