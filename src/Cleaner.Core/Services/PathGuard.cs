namespace Cleaner.Core.Services;

/// <summary>
/// Viimeinen turvaverkko: kieltäytyy poistamasta polkuja, joiden poisto olisi katastrofi,
/// vaikka sääntötiedostossa olisi virhe. Käyttö: tarkista AINA ennen poistoa.
/// </summary>
public static class PathGuard
{
    private static readonly string[] ForbiddenExact = BuildForbiddenList();

    private static string[] BuildForbiddenList()
    {
        var list = new List<string>();

        void Add(Environment.SpecialFolder folder)
        {
            var p = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(p)) list.Add(Normalize(p));
        }

        Add(Environment.SpecialFolder.Windows);
        Add(Environment.SpecialFolder.System);
        Add(Environment.SpecialFolder.SystemX86);
        Add(Environment.SpecialFolder.ProgramFiles);
        Add(Environment.SpecialFolder.ProgramFilesX86);
        Add(Environment.SpecialFolder.UserProfile);
        Add(Environment.SpecialFolder.MyDocuments);
        Add(Environment.SpecialFolder.MyPictures);
        Add(Environment.SpecialFolder.MyMusic);
        Add(Environment.SpecialFolder.MyVideos);
        Add(Environment.SpecialFolder.Desktop);
        Add(Environment.SpecialFolder.ApplicationData);
        Add(Environment.SpecialFolder.LocalApplicationData);
        Add(Environment.SpecialFolder.CommonApplicationData);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            list.Add(Normalize(Path.Combine(userProfile, "Downloads")));
            list.Add(Normalize(Path.Combine(userProfile, "OneDrive")));
        }

        return list.Distinct().ToArray();
    }

    /// <summary>
    /// Saako tämän polun poistaa? Palauttaa false, jos polku on levyn juuri,
    /// jokin suojattu järjestelmä-/käyttäjäkansio tai sen yläpuolella.
    /// </summary>
    public static bool IsSafeToDelete(string path, out string reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "Tyhjä polku.";
            return false;
        }

        string full;
        try { full = Normalize(Path.GetFullPath(path)); }
        catch (Exception ex)
        {
            reason = $"Virheellinen polku: {ex.Message}";
            return false;
        }

        // Levyn juuri, esim. "c:" tai "c:\".
        var root = Normalize(Path.GetPathRoot(full) ?? "");
        if (full == root)
        {
            reason = "Levyn juurta ei poisteta.";
            return false;
        }

        foreach (var forbidden in ForbiddenExact)
        {
            if (full == forbidden)
            {
                reason = $"Suojattu kansio: {path}";
                return false;
            }
            // Polku on suojatun kansion YLÄPUOLELLA (esim. C:\Users kun UserProfile on C:\Users\Matti).
            if (forbidden.StartsWith(full + "\\", StringComparison.Ordinal))
            {
                reason = $"Polku sisältää suojatun kansion: {path}";
                return false;
            }
        }

        reason = "";
        return true;
    }

    private static string Normalize(string path) =>
        path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
}
