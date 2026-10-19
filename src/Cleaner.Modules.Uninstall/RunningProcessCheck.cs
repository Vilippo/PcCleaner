using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Cleaner.Modules.Uninstall;

/// <summary>Etsii käynnissä olevan prosessin, jonka exe on annetun kansion alla (REQ-10).</summary>
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
                if (path is not null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    found = process.ProcessName;
            }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
        return found;
    }
}
