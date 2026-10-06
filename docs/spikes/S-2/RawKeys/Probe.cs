using System.Runtime.InteropServices;

namespace Spike.RawKeys;

internal static class Probe
{
    public static int Run()
    {
        Console.WriteLine("S-2 probe: environment and console capability");
        Console.WriteLine($"os: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"arch: {RuntimeInformation.OSArchitecture}");
        Console.WriteLine($"term: {Environment.GetEnvironmentVariable("TERM") ?? "(unset)"}");
        Console.WriteLine(
            $"term_program: {Environment.GetEnvironmentVariable("TERM_PROGRAM") ?? "(unset)"}"
        );
        Console.WriteLine($"msystem: {Environment.GetEnvironmentVariable("MSYSTEM") ?? "(unset)"}");
        Console.WriteLine(
            $"wt_session: {Environment.GetEnvironmentVariable("WT_SESSION") ?? "(unset)"}"
        );
        Console.WriteLine(
            $"sessionname: {Environment.GetEnvironmentVariable("SESSIONNAME") ?? "(unset)"}"
        );
        Console.WriteLine($"stdin_redirected: {Console.IsInputRedirected}");
        Console.WriteLine($"stdout_redirected: {Console.IsOutputRedirected}");
        Console.WriteLine($"stderr_redirected: {Console.IsErrorRedirected}");
        Console.WriteLine($"windows_console_attached: {WindowsConsoleAttached()}");
        Console.WriteLine($"key_available: {KeyAvailable()}");
        Console.WriteLine("next: run without arguments to test interactive Console.ReadKey");
        return 0;
    }

    private static string WindowsConsoleAttached()
    {
        if (!OperatingSystem.IsWindows())
            return "n/a (not windows)";
        try
        {
            var handle = GetStdHandle(-10);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return "no (GetStdHandle returned null/invalid)";
            }
            return GetConsoleMode(handle, out _)
                ? "yes (GetConsoleMode on stdin succeeded)"
                : $"no (GetConsoleMode failed, win32 error {Marshal.GetLastWin32Error()})";
        }
        catch (Exception ex)
        {
            return $"error: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string KeyAvailable()
    {
        try
        {
            return Console.KeyAvailable ? "yes" : "no (no key buffered)";
        }
        catch (Exception ex)
        {
            return $"throws {ex.GetType().Name}: {ex.Message}";
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
}
