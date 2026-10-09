using System;

namespace WordTool;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: wordtool <text>");
            return 2;
        }

        string text = string.Join(' ', args);
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine($"words: {words.Length}");
        return 0;
    }
}
