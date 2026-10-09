using System;

namespace GreetingApp;

public static class Program
{
    public static void Main()
    {
        Console.WriteLine(Greeter.Greet("Ada"));
        Console.WriteLine(Formatter.Title("report"));
    }
}
