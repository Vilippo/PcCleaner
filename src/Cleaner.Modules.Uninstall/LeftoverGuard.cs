using System.Runtime.Versioning;
using Cleaner.Core.Services;

namespace Cleaner.Modules.Uninstall;

/// <summary>
/// Jäännöskohteiden tunnistetyyppikohtainen vartija (REQ-8). Tarkistetaan ennen jokaista siirtoa tai poistoa,
/// myös yläkansion ja yläavaimen siivouksessa (REQ-9), vaikka kohde olisi tullut omasta skannauksesta.
/// </summary>
[SupportedOSPlatform("windows")]
public static class LeftoverGuard
{
    /// <summary>Segmentit, joita ei sallita juuren jälkeisessä osassa (RegistryGuard.ForbiddenSegments lisäksi).</summary>
    private static readonly string[] DeniedSegments =
        { "Microsoft", "Classes", "Policies", "WOW6432Node", "Clients", "RegisteredApplications" };

    public static bool IsAllowed(LeftoverKind kind, string path, string? valueName, InstalledProgram? target,
        LeftoverScanOptions o, out string reason)
    {
        return kind switch
        {
            LeftoverKind.Dir or LeftoverKind.File => IsAllowedPath(path, target, o, out reason),
            LeftoverKind.Key => IsAllowedKey(path, o, out reason),
            LeftoverKind.Value => IsAllowedValue(path, valueName, o, out reason),
            _ => Fail("Tuntematon kohdetyyppi.", out reason)
        };
    }

    // ---------- Kansiot ja tiedostot ----------

    private static bool IsAllowedPath(string path, InstalledProgram? target, LeftoverScanOptions o, out string reason)
    {
        var full = NormalizePath(path);
        if (full is null) return Fail("Virheellinen polku.", out reason);

        var folders = o.BaseFolders.Select(b => b.Path).Concat(o.ShortcutFolders.Select(s => s.Path))
            .Select(NormalizePath).OfType<string>().ToList();

        foreach (var folder in folders)
        {
            if (full.Equals(folder, StringComparison.OrdinalIgnoreCase))
                return Fail($"Peruskansiota ei poisteta: {full}", out reason);
            if (IsUnder(folder, full))
                return Fail($"Polku sisältää peruskansion: {full}", out reason);
        }

        var install = InstallLocationOrNull(target?.InstallLocation);
        var inside = folders.Any(f => IsUnder(full, f))
            || (install is not null && (full.Equals(install, StringComparison.OrdinalIgnoreCase) || IsUnder(full, install)));
        if (!inside) return Fail("Polku ei ole peruskansion, pikakuvakekansion eikä asennuskansion alla.", out reason);

        return PathGuard.IsSafeToDelete(full, out reason);
    }

    // ---------- Rekisteriavaimet ----------

    private static bool IsAllowedKey(string path, LeftoverScanOptions o, out string reason)
    {
        var key = NormalizeKey(path);
        if (key is null) return Fail("Virheellinen rekisteripolku.", out reason);

        if (MatchesAny(key, o.RegistryRoots, maxSegments: 2)
            || MatchesAny(key, o.UninstallRoots, maxSegments: 1)
            || MatchesAny(key, o.AppPathsRoots, maxSegments: 1)
            || MatchesAny(key, o.ClassesRoots, maxSegments: 1))
            return Ok(out reason);

        return Fail(@"Avain ei ole sallittua muotoa (juuri\X, juuri\X\Y, Uninstall\X, App Paths\X tai Classes\X) tai sisältää suojatun segmentin.", out reason);
    }

    // ---------- Rekisteriarvot ----------

    private static bool IsAllowedValue(string path, string? valueName, LeftoverScanOptions o, out string reason)
    {
        if (valueName is null) return Fail("Arvon nimi puuttuu.", out reason);
        var key = NormalizeKey(path);
        if (key is null) return Fail("Virheellinen rekisteripolku.", out reason);

        if (o.RunKeys.Any(r => key.Equals(r.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            return Ok(out reason);

        foreach (var root in o.RegistryRoots)
        {
            var r = root.TrimEnd('\\');
            if (TryRemainder(key, r, out var segments) && segments.Length <= o.MaxRegistryDepth && !HasDeniedSegment(segments))
                return Ok(out reason);
        }

        // Juuren omat arvot (syvyys 0) eivät kelpaa: vienti koskisi koko juurihaaraa (esim. HKCU\Software).
        return Fail($"Arvon avain ei ole sallitun juuren alla enintään {o.MaxRegistryDepth} tason syvyydessä eikä Run-avain, tai sisältää suojatun segmentin.", out reason);
    }

    // ---------- Apurit ----------

    /// <summary>Avain on muotoa &lt;juuri&gt;\S1[\S2...] enintään <paramref name="maxSegments"/> segmentillä ilman kiellettyjä segmenttejä.</summary>
    private static bool MatchesAny(string key, IEnumerable<string> roots, int maxSegments)
    {
        foreach (var root in roots)
        {
            if (TryRemainder(key, root.TrimEnd('\\'), out var segments)
                && segments.Length <= maxSegments
                && !HasDeniedSegment(segments))
                return true;
        }
        return false;
    }

    /// <summary>Juuren jälkeiset segmentit (vähintään yksi, ei tyhjiä).</summary>
    private static bool TryRemainder(string key, string root, out string[] segments)
    {
        segments = Array.Empty<string>();
        if (root.Length == 0 || !key.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        segments = key[(root.Length + 1)..].Split('\\');
        return segments.Length > 0 && segments.All(s => s.Length > 0);
    }

    private static bool HasDeniedSegment(IEnumerable<string> segments) =>
        segments.Any(s => RegistryGuard.ForbiddenSegments.Contains(s, StringComparer.OrdinalIgnoreCase)
                          || DeniedSegments.Contains(s, StringComparer.OrdinalIgnoreCase));

    private static string? NormalizeKey(string path)
    {
        if (!RegistryPaths.TryParse(path, out _, out var sub) || sub.Length == 0) return null;
        return path.Trim().TrimEnd('\\');
    }

    /// <summary>Normalisoitu InstallLocation, tai null jos se puuttuu, on virheellinen tai on levyn juuri (esim. C:\).</summary>
    internal static string? InstallLocationOrNull(string? installLocation)
    {
        var full = NormalizePath(installLocation);
        if (full is null) return null;
        var root = Path.GetPathRoot(full)?.TrimEnd('\\');
        return root is not null && root.Equals(full, StringComparison.OrdinalIgnoreCase) ? null : full;
    }

    /// <summary>Täysi polku ilman loppukenoa; suhteellinen tai virheellinen polku → null.</summary>
    internal static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var trimmed = path.Trim().Trim('"').Trim();
            if (!Path.IsPathFullyQualified(trimmed)) return null;
            var full = Path.GetFullPath(trimmed).TrimEnd('\\');
            return full.Length == 0 ? null : full;
        }
        catch (Exception) { return null; }
    }

    private static bool IsUnder(string path, string parent) =>
        path.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);

    private static bool Ok(out string reason)
    {
        reason = "";
        return true;
    }

    private static bool Fail(string message, out string reason)
    {
        reason = message;
        return false;
    }
}
