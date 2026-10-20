using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Cleaner.Modules.Uninstall;

/// <summary>Etsii käynnissä olevan prosessin, jonka exe on annetun kansion alla tai täsmälleen annettu tiedosto (REQ-10).</summary>
[SupportedOSPlatform("windows")]
public static class RunningProcessCheck
{
    /// <summary>Ensimmäisen kansion alla ajossa olevan prosessin nimi tai null. Prosessit, joiden polkua ei voi lukea, ohitetaan.</summary>
    public static string? FindRunningUnder(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;

        string prefix;
        try
        {
            prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "\\";
        }
        catch (Exception) { return null; }

        return Find(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Prosessin nimi, jonka exe on täsmälleen annettu tiedosto, tai null. Pikakuvakkeet ja datatiedostot eivät koskaan osu.</summary>
    public static string? FindRunningFile(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;

        string full;
        try { full = Path.GetFullPath(file); }
        catch (Exception) { return null; }

        return Find(path => path.Equals(full, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Find(Func<string, bool> matches)
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch (Exception) { return null; }

        var selfId = Environment.ProcessId;
        string? found = null;
        foreach (var process in processes)
        {
            try
            {
                if (found is not null || process.Id == selfId) continue;
                var path = process.MainModule?.FileName;
                if (path is not null && matches(path))
                    found = process.ProcessName;
            }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
        return found;
    }
}
