namespace Cleaner.Core.Services;

public static class ByteFormatter
{
    private static readonly string[] Units = { "t", "kt", "Mt", "Gt", "Tt" };

    /// <summary>Muotoilee tavumäärän luettavaksi, esim. 1 536 000 → "1,46 Mt".</summary>
    public static string Format(long bytes)
    {
        if (bytes <= 0) return "0 t";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} t" : $"{value:0.##} {Units[unit]}";
    }
}
