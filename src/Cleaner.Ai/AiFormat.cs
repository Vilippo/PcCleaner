using System.Globalization;

namespace Cleaner.Ai;

/// <summary>
/// Kielestä riippumaton tavumuotoilija mallille lähetettävään tekstiin. <c>ByteFormatter</c> seuraa
/// <c>Loc.Current</c>-kieltä (suomeksi "Mt" ja desimaalipilkku), joten sitä ei käytetä tässä projektissa.
/// </summary>
public static class AiFormat
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    /// <summary>Esim. "0 B", "512 B", "1.5 KB", "61.9 MB", "1.2 GB". Negatiivinen arvo → "0 B".</summary>
    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
            : $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {Units[unit]}";
    }
}
