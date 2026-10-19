namespace Cleaner.Modules.RegistryCleaner;

/// <summary>
/// Poimii komentorivistä suoritettavan tiedoston polun ja päättelee, puuttuuko se.
/// Varovainen: epäselvä tapaus tulkitaan aina "ei puutu".
/// </summary>
public static class CommandLineParser
{
    /// <summary>
    /// "C:\X\u.exe" /S → C:\X\u.exe; C:\Program Files\X\u.exe /S → C:\Program Files\X\u.exe;
    /// %SystemRoot%\notepad.exe %1 → laajennettu polku. Palauttaa null, jos komentoa ei voi tulkita
    /// tai siihen jää tuntemattomia %-muuttujia.
    /// </summary>
    public static string? ExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        string candidate;

        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end <= 1) return null;
            candidate = text[1..end];
        }
        else
        {
            var exeIdx = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx >= 0)
            {
                candidate = text[..(exeIdx + 4)];
            }
            else
            {
                var space = text.IndexOf(' ');
                candidate = space < 0 ? text : text[..space];
            }
        }

        candidate = candidate.Trim();
        if (candidate.Length == 0 || candidate.Contains('%')) return null;
        return candidate;
    }

    /// <summary>
    /// Puuttuuko tiedosto varmasti. True vain, kun polku on absoluuttinen, sen asema on olemassa,
    /// polku ei osoita WindowsApps-kansioon (ei lukuoikeutta) eikä tiedostoa tai kansiota ole.
    /// </summary>
    public static bool IsDefinitelyMissing(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            if (!Path.IsPathRooted(path)) return false;
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false; // verkkopolku: ei tarkisteta

            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false; // irrotettu asema

            if (path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return false;

            if (File.Exists(path) || Directory.Exists(path)) return false;

            // "C:\Windows\system32\perfmon /sys" → pääte puuttuu komennosta, tiedosto on perfmon.exe.
            if (!Path.HasExtension(path) && (File.Exists(path + ".exe") || File.Exists(path + ".com")))
                return false;

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Tiedostonimi ilman polkua pieninä kirjaimina, esim. "msiexec.exe".</summary>
    public static string FileNameLower(string path)
    {
        try { return Path.GetFileName(path).ToLowerInvariant(); }
        catch (Exception) { return path.ToLowerInvariant(); }
    }
}
