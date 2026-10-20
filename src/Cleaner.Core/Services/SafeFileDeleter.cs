using Cleaner.Core.Logging;
using Cleaner.Core.Localization;

namespace Cleaner.Core.Services;

/// <summary>
/// Poistaa tiedostoja ja kansioita turvallisesti:
/// PathGuard-tarkistus, kuiva ajo, lukittujen ohitus, vain luku -lipun poisto, lokitus.
/// Kaikki moduulit käyttävät tätä sen sijaan että kutsuisivat File.Delete suoraan.
/// </summary>
public sealed class SafeFileDeleter
{
    private readonly ICleanerLogger _log;
    private readonly bool _dryRun;

    public SafeFileDeleter(ICleanerLogger log, bool dryRun)
    {
        _log = log;
        _dryRun = dryRun;
    }

    /// <summary>Poistaa yhden tiedoston. Palauttaa true jos poistettiin (tai kuivassa ajossa olisi poistettu).</summary>
    public bool TryDeleteFile(string path, out string failReason)
    {
        if (!PathGuard.IsSafeToDelete(path, out failReason))
        {
            _log.Warning(Loc.T($"BLOCKED {path}: {failReason}", $"ESTETTY {path}: {failReason}"));
            return false;
        }

        if (_dryRun)
        {
            _log.Debug(Loc.T($"[DRY RUN] Would delete file: {path}", $"[KUIVA] Poistettaisiin tiedosto: {path}"));
            failReason = "";
            return true;
        }

        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);

            File.Delete(path);
            _log.Info(Loc.T($"Deleted file: {path}", $"Poistettu tiedosto: {path}"));
            failReason = "";
            return true;
        }
        catch (FileNotFoundException)
        {
            // Joku muu ehti poistaa – ei virhe.
            failReason = "";
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            failReason = Loc.T("Access denied", "Ei oikeuksia");
        }
        catch (IOException ex)
        {
            // Tyypillisesti tiedosto on toisen prosessin käytössä.
            failReason = Loc.T($"In use or I/O error: {ex.Message}", $"Käytössä tai IO-virhe: {ex.Message}");
        }
        catch (Exception ex)
        {
            failReason = ex.Message;
        }

        _log.Warning(Loc.T($"Skipped {path}: {failReason}", $"Ohitettu {path}: {failReason}"));
        return false;
    }

    /// <summary>Poistaa kansion, jos se on tyhjä. Ei koskaan poista ei-tyhjää kansiota.</summary>
    public bool TryDeleteEmptyDirectory(string path)
    {
        if (!PathGuard.IsSafeToDelete(path, out var reason))
        {
            _log.Warning(Loc.T($"BLOCKED {path}: {reason}", $"ESTETTY {path}: {reason}"));
            return false;
        }

        try
        {
            if (!Directory.Exists(path)) return false;
            if (Directory.EnumerateFileSystemEntries(path).Any()) return false;

            if (_dryRun)
            {
                _log.Debug(Loc.T($"[DRY RUN] Would delete empty folder: {path}", $"[KUIVA] Poistettaisiin tyhjä kansio: {path}"));
                return true;
            }

            Directory.Delete(path, recursive: false);
            _log.Info(Loc.T($"Deleted empty folder: {path}", $"Poistettu tyhjä kansio: {path}"));
            return true;
        }
        catch (Exception ex)
        {
            _log.Debug(Loc.T($"Skipped deleting folder {path}: {ex.Message}", $"Kansion poisto ohitettu {path}: {ex.Message}"));
            return false;
        }
    }
}
