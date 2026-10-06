namespace Spike.RawKeys;

internal static class Interactive
{
    public static int Run()
    {
        Console.WriteLine(
            "interactive: Console.ReadKey(intercept: true); press keys, Esc or Ctrl+C exits"
        );
        try
        {
            while (true)
            {
                var info = Console.ReadKey(true);
                Console.WriteLine(Describe(info));
                if (
                    info.Key == ConsoleKey.Escape
                    || (
                        info.Key == ConsoleKey.C && info.Modifiers.HasFlag(ConsoleModifiers.Control)
                    )
                )
                {
                    break;
                }
            }
            Console.WriteLine("interactive: ended normally");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"readkey-failed: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine("interactive: raw-key reading is not available in this terminal");
            return 2;
        }
    }

    private static string Describe(ConsoleKeyInfo info)
    {
        var ch = info.KeyChar is >= (char)0x20 and not (char)0x7F
            ? $"'{info.KeyChar}'"
            : $"\\u{(int)info.KeyChar:x4}";
        return $"readkey key={info.Key} char={ch} mods={info.Modifiers}";
    }
}
