namespace Cleaner.Modules.Processes;

/// <summary>
/// Päättää, mitä prosesseja ei koskaan listata eikä lopeteta. Sama tarkistus ajetaan skannauksessa ja
/// uudelleen ennen lopetusta tuoreelle tiedolle.
/// </summary>
public static class ProcessGuard
{
    /// <summary>Windows-kansio. Testit ylikirjoittavat tämän.</summary>
    internal static string WindowsDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>Syy suomeksi tai null, kun prosessia saa listata ja lopettaa.</summary>
    public static string? Protects(ProcessInfo info, ProcessSnapshot snapshot)
    {
        // OwnParentPid kattaa myös jo päättyneen vanhemman; OwnAncestorPids koko elossa olevan ketjun (pääte, jonka
        // sisältä PcCleaner ajettiin, ei saa päätyä listalle).
        if (info.Pid == snapshot.OwnPid || info.Pid == snapshot.OwnParentPid || snapshot.OwnAncestorPids.Contains(info.Pid))
            return "Oma prosessi";
        if (info.Pid <= 4)
            return "Järjestelmäprosessi";
        if (info.SessionId != snapshot.CurrentSessionId)
            return "Toinen istunto tai palvelu";
        if (info.ExePath is null || info.StartFileTime == 0)
            return "Tietoja ei voitu lukea";
        if (IsUnderWindowsDirectory(info.ExePath))
            return "Windows-kansion prosessi";
        if (info.IsCritical)
            return "Kriittinen prosessi";
        if (!info.CanTerminate)
            return "Ei oikeuksia lopettaa";
        return null;
    }

    private static bool IsUnderWindowsDirectory(string exePath)
    {
        var dir = WindowsDirectory;
        if (string.IsNullOrEmpty(dir)) return false;

        var prefix = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return exePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
