namespace StatsApp;

public static class Stats
{
    public static double Mean(int[] values)
    {
        int sum = 0;
        foreach (int value in values)
        {
            sum += value;
        }

        return sum / values.Length;
    }
}
