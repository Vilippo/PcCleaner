using System.Diagnostics;

namespace Cleaner.Core.Services;

/// <summary>Tarkistaa, onko jokin prosessi käynnissä (esim. selain ennen välimuistin poistoa).</summary>
public static class ProcessChecker
{
    public static bool IsAnyRunning(IEnumerable<string> processNames, out string runningName)
    {
        foreach (var name in processNames)
        {
            var clean = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;

            Process[] found;
            try { found = Process.GetProcessesByName(clean); }
            catch { continue; }

            var any = found.Length > 0;
            foreach (var p in found) p.Dispose();

            if (any)
            {
                runningName = clean;
                return true;
            }
        }

        runningName = "";
        return false;
    }
}
