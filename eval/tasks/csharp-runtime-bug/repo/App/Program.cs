using System;
using System.Globalization;

namespace StatsApp;

public static class Program
{
    public static void Main()
    {
        int[] values = [1, 2, 3, 4];
        Console.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"Mean: {Stats.Mean(values):0.0}")
        );
    }
}
