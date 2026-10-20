using System.Globalization;

namespace Cleaner.Modules.Processes;

/// <summary>Kohteen tunniste "processes:&lt;pid&gt;:&lt;startFileTime&gt;". Käynnistysaika erottaa kierrätetyn PID:n.</summary>
public static class ProcessItemId
{
    public const string Prefix = "processes";

    public static string Make(int pid, long startFileTime)
        => string.Create(CultureInfo.InvariantCulture, $"{Prefix}:{pid}:{startFileTime}");

    public static bool TryParse(string id, out int pid, out long startFileTime)
    {
        pid = 0;
        startFileTime = 0;
        if (string.IsNullOrEmpty(id)) return false;

        var parts = id.Split(':');
        if (parts.Length != 3 || parts[0] != Prefix) return false;

        return int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out pid)
            && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out startFileTime);
    }
}
