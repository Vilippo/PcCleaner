using System.Runtime.Versioning;
using Cleaner.Core.Localization;

namespace Cleaner.Core.Services;

/// <summary>
/// Rekisterin viimeinen turvaverkko (vrt. PathGuard): kieltäytyy koskemasta avaimiin, jotka eivät ole
/// sallittujen juurten alla, ja avaimiin, joiden polussa on COM-, palvelu- tai ajuriavain.
/// Tarkistetaan AINA ennen poistoa, vaikka kohde olisi tullut omasta skannauksesta.
/// </summary>
[SupportedOSPlatform("windows")]
public static class RegistryGuard
{
    /// <summary>Avainnimet, joita ei koskaan poisteta eikä niiden alta mitään (ARCHITECTURE 6.3 + AppID).</summary>
    public static readonly string[] ForbiddenSegments =
    {
        "CLSID", "Interface", "TypeLib", "AppID", "Services", "Drivers", "SharedDLLs", "Fonts"
    };

    /// <summary>
    /// Saako avaimeen koskea. <paramref name="allowedRoots"/> ovat moduulin skannausjuuret;
    /// kohteen on oltava jonkin juuren ALLA (ei itse juuri), ellei <paramref name="allowExactRoot"/> ole tosi
    /// (MRU-listat tyhjennetään, avain säilyy).
    /// </summary>
    public static bool IsAllowed(string path, IEnumerable<string> allowedRoots, bool allowExactRoot, out string reason)
    {
        if (!RegistryPaths.TryParse(path, out _, out var sub))
        {
            reason = Loc.T("Invalid registry path.", "Virheellinen rekisteripolku.");
            return false;
        }

        if (sub.Length == 0)
        {
            reason = Loc.T("The hive root is never deleted.", "Hiven juurta ei koskaan poisteta.");
            return false;
        }

        foreach (var segment in sub.Split('\\'))
        {
            if (ForbiddenSegments.Any(f => f.Equals(segment, StringComparison.OrdinalIgnoreCase)))
            {
                reason = Loc.T($"Protected key in path: {segment}", $"Suojattu avain polussa: {segment}");
                return false;
            }
        }

        var normalized = path.TrimEnd('\\');
        foreach (var root in allowedRoots)
        {
            var r = root.TrimEnd('\\');
            if (allowExactRoot && normalized.Equals(r, StringComparison.OrdinalIgnoreCase))
            {
                reason = "";
                return true;
            }
            if (normalized.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))
            {
                reason = "";
                return true;
            }
        }

        reason = Loc.T("Path is not under any allowed root.", "Polku ei ole minkään sallitun juuren alla.");
        return false;
    }
}
