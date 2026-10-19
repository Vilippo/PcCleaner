namespace Cleaner.Modules.Uninstall;

/// <summary>
/// Uninstall-komentorivin purku. Logiikka on kopioitu Registry-moduulin CommandLineParserista,
/// jotta moduulit eivät viittaa toisiinsa.
/// </summary>
public static class UninstallCommandLine
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
    /// Komentorivin loppuosa suoritettavan tiedoston jälkeen (trimmattu, lainausmerkit säilyvät).
    /// "MsiExec.exe /X{GUID}" → "/X{GUID}". Null, jos komento on tyhjä tai exeä ei saa poimittua.
    /// </summary>
    public static string? ExtractArguments(string? command)
    {
        if (ExtractExecutable(command) is null) return null;

        var text = Environment.ExpandEnvironmentVariables(command!.Trim());
        int rest;
        if (text.StartsWith('"'))
        {
            rest = text.IndexOf('"', 1) + 1;
        }
        else
        {
            var exeIdx = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx >= 0)
            {
                rest = exeIdx + 4;
            }
            else
            {
                var space = text.IndexOf(' ');
                rest = space < 0 ? text.Length : space;
            }
        }
        return text[rest..].Trim();
    }

    /// <summary>Tiedostonimi ilman polkua pieninä kirjaimina, esim. "msiexec.exe".</summary>
    public static string FileNameLower(string path)
    {
        try { return Path.GetFileName(path).ToLowerInvariant(); }
        catch (Exception) { return path.ToLowerInvariant(); }
    }
}
