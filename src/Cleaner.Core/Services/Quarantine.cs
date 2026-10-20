using System.Runtime.CompilerServices;
using Cleaner.Core.Logging;
using Cleaner.Core.Localization;

[assembly: InternalsVisibleTo("Cleaner.Tests")]

namespace Cleaner.Core.Services;

/// <summary>
/// Karanteeni: poistettavat kansiot ja tiedostot siirretään talteen ennen poistoa.
/// Yksi kansio per ajo: %LOCALAPPDATA%\PcCleaner\quarantine\&lt;aikaleima&gt;\NNN\&lt;alkuperäinen nimi&gt; + manifest.txt.
/// Palautus siirtää kohteet takaisin alkuperäisiin paikkoihinsa.
/// </summary>
public sealed class Quarantine
{
    public static string AccessDeniedMessage => Loc.T("Access denied (requires administrator rights)", "Ei oikeuksia (tarvitsee järjestelmänvalvojan oikeudet)");

    public sealed record Entry(string Index, string OriginalPath, string QuarantinedPath, bool IsDirectory);
    public sealed record SessionInfo(string Timestamp, string Directory, int EntryCount);
    public sealed record RestoreResult(int Restored, int Skipped, IReadOnlyList<string> Messages);

    private enum MoveResult { Moved, NotMoved, CopiedButSourceRemains }

    /// <summary>Testisauma: korvaa asemavertailun (eri aseman haaran testaus yhdellä asemalla).</summary>
    internal static Func<string, string, bool>? SameDriveOverride { get; set; }

    private readonly ICleanerLogger _log;
    private readonly List<Entry> _entries = new();
    private int _counter;

    public string Timestamp { get; }
    public string Directory { get; }
    public bool HasEntries => _entries.Count > 0;

    /// <summary>Kansio luodaan laiskasti ensimmäisessä siirrossa.</summary>
    public Quarantine(string root, string timestamp, ICleanerLogger log)
    {
        _log = log;
        Timestamp = timestamp;
        Directory = Path.Combine(root, timestamp);
    }

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcCleaner", "quarantine");

    public bool TryMoveDirectory(string path, out string quarantinedPath, out string error) =>
        TryMove(path, isDirectory: true, out quarantinedPath, out error);

    public bool TryMoveFile(string path, out string quarantinedPath, out string error) =>
        TryMove(path, isDirectory: false, out quarantinedPath, out error);

    private bool TryMove(string path, bool isDirectory, out string quarantinedPath, out string error)
    {
        quarantinedPath = "";
        if (!PathGuard.IsSafeToDelete(path, out error))
        {
            _log.Warning(Loc.T($"BLOCKED {path}: {error}", $"ESTETTY {path}: {error}"));
            return false;
        }

        string source;
        try { source = Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch (Exception ex)
        {
            error = Loc.T($"Invalid path: {ex.Message}", $"Virheellinen polku: {ex.Message}");
            return false;
        }

        if (isDirectory ? !System.IO.Directory.Exists(source) : !File.Exists(source))
        {
            error = Loc.T($"Path does not exist: {source}", $"Polkua ei ole: {source}");
            return false;
        }

        // Jo levyllä oleva NNN-kansio (esim. siivoamatta jäänyt jäänne) ohitetaan, ettei kohteita sekoiteta.
        while (System.IO.Directory.Exists(Path.Combine(Directory, $"{_counter + 1:000}"))) _counter++;

        var index = $"{_counter + 1:000}";
        var slot = Path.Combine(Directory, index);
        var target = Path.Combine(slot, Path.GetFileName(source));

        var sessionExisted = System.IO.Directory.Exists(Directory);
        switch (MoveItem(source, target, isDirectory, slot, out error))
        {
            case MoveResult.Moved:
                Record();
                quarantinedPath = target;
                _log.Info(Loc.T($"Moved to quarantine: {source} → {target}", $"Karanteeniin: {source} → {target}"));
                return true;

            case MoveResult.CopiedButSourceRemains:
                // Kopio on kokonaan karanteenissa: paikka on käytetty ja merkintä kirjataan, jotta palautus löytää sen.
                Record();
                quarantinedPath = target;
                error = Loc.T(
                    $"Failed to delete the original, the copy is in quarantine: {target}. {error}",
                    $"Alkuperäisen poisto epäonnistui, kopio on karanteenissa: {target}. {error}");
                _log.Warning(Loc.T($"Quarantine move incomplete {source}: {error}", $"Karanteenisiirto kesken {source}: {error}"));
                return false;

            default:
                if (!sessionExisted) TryDeleteEmptyDirectory(Directory);
                _log.Warning(Loc.T($"Quarantine move failed {source}: {error}", $"Karanteenisiirto epäonnistui {source}: {error}"));
                return false;
        }

        void Record()
        {
            _counter++;
            _entries.Add(new Entry(index, source, target, isDirectory));
        }
    }

    /// <summary>
    /// Siirtää kohteen. Sama asema: Directory.Move / File.Move. Eri asema (EDGE-5): kopio ja
    /// alkuperäisen poisto vasta onnistuneen kopion jälkeen. NotMoved: keskeneräinen kopio poistetaan ja
    /// <paramref name="createdDir"/> poistetaan, jos tämä kutsu loi sen ja se jäi tyhjäksi.
    /// CopiedButSourceRemains: kopio valmis, lähteen poisto epäonnistui (error = syy); mitään ei siivota.
    /// </summary>
    private static MoveResult MoveItem(string source, string target, bool isDirectory, string createdDir, out string error)
    {
        error = "";
        var dirExisted = System.IO.Directory.Exists(createdDir);
        var targetExisted = System.IO.Directory.Exists(target) || File.Exists(target);
        var copyStarted = false;
        var copied = false;
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (IsSameDrive(source, target))
            {
                if (isDirectory) System.IO.Directory.Move(source, target);
                else File.Move(source, target);
                return MoveResult.Moved;
            }

            copyStarted = true;
            if (isDirectory) CopyDirectory(source, target);
            else File.Copy(source, target);
            copied = true;

            if (isDirectory) System.IO.Directory.Delete(source, recursive: true);
            else File.Delete(source);
            return MoveResult.Moved;
        }
        catch (UnauthorizedAccessException)
        {
            error = AccessDeniedMessage;
        }
        catch (Exception ex)
        {
            error = copied ? ex.Message : Loc.T($"Move failed: {ex.Message}", $"Siirto epäonnistui: {ex.Message}");
        }

        // Kopio valmis, mutta lähteen poisto epäonnistui: kopiota ei poisteta, koska lähde voi olla
        // jo osittain poistettu.
        if (copied) return MoveResult.CopiedButSourceRemains;

        // Keskeneräinen kopio pois (vain tämän kutsun luoma, ei koskaan valmiiksi ollutta kohdetta).
        if (copyStarted && !targetExisted) TryDeletePath(target, isDirectory);
        if (!dirExisted) TryDeleteEmptyDirectory(createdDir);
        return MoveResult.NotMoved;
    }

    private static bool IsSameDrive(string a, string b) =>
        SameDriveOverride?.Invoke(a, b) ?? string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    private static void CopyDirectory(string source, string target)
    {
        System.IO.Directory.CreateDirectory(target);
        foreach (var file in System.IO.Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in System.IO.Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static void TryDeletePath(string path, bool isDirectory)
    {
        try
        {
            if (!PathGuard.IsSafeToDelete(path, out _)) return;
            if (isDirectory && System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, recursive: true);
            else if (!isDirectory && File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (!PathGuard.IsSafeToDelete(path, out _)) return;
            if (System.IO.Directory.Exists(path) && !System.IO.Directory.EnumerateFileSystemEntries(path).Any())
                System.IO.Directory.Delete(path, recursive: false);
        }
        catch { }
    }

    /// <summary>Kirjoittaa manifest.txt:n: siirretyt kohteet ja käsitellyt kohteet.</summary>
    public void WriteManifest(IEnumerable<string> processedItems)
    {
        if (!HasEntries) return;
        // manifest.txt on pysyvä tiedostomuoto: sisältö pysyy suomeksi kielestä riippumatta, jotta
        // vanhat ja uudet karanteenit luetaan samalla tavalla. Älä käännä näitä rivejä.
        var lines = new List<string>
        {
            $"PC Cleaner karanteeni {Timestamp}",
            $"Palautus: pccleaner restore {Timestamp}",
            "",
            "Siirretyt kohteet:"
        };
        lines.AddRange(_entries.Select(e =>
            $"{e.Index}\t{(e.IsDirectory ? "D" : "F")}\t{e.OriginalPath}\t{e.QuarantinedPath}"));
        lines.Add("");
        lines.Add("Käsitellyt kohteet:");
        lines.AddRange(processedItems);
        File.WriteAllLines(Path.Combine(Directory, "manifest.txt"), lines);
    }

    /// <summary>Sessiot, joissa on manifest.txt, uusin ensin.</summary>
    public static IReadOnlyList<SessionInfo> List(string? root = null)
    {
        root ??= DefaultRoot;
        if (!System.IO.Directory.Exists(root)) return Array.Empty<SessionInfo>();

        var list = new List<SessionInfo>();
        foreach (var dir in System.IO.Directory.GetDirectories(root).OrderByDescending(d => d))
        {
            if (!File.Exists(Path.Combine(dir, "manifest.txt"))) continue;
            list.Add(new SessionInfo(Path.GetFileName(dir), dir, ReadManifest(dir).Count));
        }
        return list;
    }

    /// <summary>Lukee manifestista vain merkintärivit (NNN&lt;TAB&gt;D|F&lt;TAB&gt;alkuperäinen&lt;TAB&gt;karanteeni).</summary>
    public static IReadOnlyList<Entry> ReadManifest(string sessionDirectory)
    {
        var manifest = Path.Combine(sessionDirectory, "manifest.txt");
        if (!File.Exists(manifest)) return Array.Empty<Entry>();

        var entries = new List<Entry>();
        foreach (var line in File.ReadAllLines(manifest))
        {
            var parts = line.Split('\t');
            if (parts.Length != 4) continue;
            if (parts[0].Length != 3 || !parts[0].All(char.IsAsciiDigit)) continue;
            if (parts[1] is not ("D" or "F")) continue;
            entries.Add(new Entry(parts[0], parts[2], parts[3], parts[1] == "D"));
        }
        return entries;
    }

    /// <summary>
    /// Siirtää karanteenin kohteet takaisin. Puuttuvat yläkansiot luodaan. Jos alkuperäinen polku on
    /// jo olemassa, kohde jää karanteeniin ja viesti kertoo polun (EDGE-6).
    /// </summary>
    public static RestoreResult Restore(string timestamp, ICleanerLogger log, string? root = null)
    {
        root ??= DefaultRoot;
        var dir = Path.Combine(root, timestamp);
        var messages = new List<string>();

        if (!File.Exists(Path.Combine(dir, "manifest.txt")))
        {
            messages.Add(Loc.T($"Quarantine not found: {dir}", $"Karanteenia ei ole: {dir}"));
            return new RestoreResult(0, 0, messages);
        }

        var sessionPrefix = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
        int restored = 0, skipped = 0;
        foreach (var manifestEntry in ReadManifest(dir))
        {
            // Manifestiin ei luoteta: karanteenipolun on oltava session kansiossa.
            var quarantined = FullPathOrEmpty(manifestEntry.QuarantinedPath);
            if (!quarantined.StartsWith(sessionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                Report(Loc.T(
                    $"Skipped, quarantine path is not inside the session folder: {manifestEntry.QuarantinedPath}",
                    $"Ohitettu, karanteenipolku ei ole session kansiossa: {manifestEntry.QuarantinedPath}"));
                continue;
            }
            var entry = manifestEntry with { QuarantinedPath = quarantined };

            var present = entry.IsDirectory
                ? System.IO.Directory.Exists(entry.QuarantinedPath)
                : File.Exists(entry.QuarantinedPath);
            if (!present)
            {
                skipped++;
                Report(Loc.T($"Skipped, quarantined item not found: {entry.QuarantinedPath}", $"Ohitettu, karanteenikohdetta ei löydy: {entry.QuarantinedPath}"));
                continue;
            }

            if (System.IO.Directory.Exists(entry.OriginalPath) || File.Exists(entry.OriginalPath))
            {
                skipped++;
                Report(Loc.T($"Skipped, path already exists: {entry.OriginalPath}", $"Ohitettu, polku on jo olemassa: {entry.OriginalPath}"));
                continue;
            }

            if (!PathGuard.IsSafeToDelete(entry.QuarantinedPath, out var reason))
            {
                skipped++;
                Report(Loc.T($"BLOCKED {entry.QuarantinedPath}: {reason}", $"ESTETTY {entry.QuarantinedPath}: {reason}"));
                continue;
            }

            var parent = Path.GetDirectoryName(entry.OriginalPath)!;
            var moved = MoveItem(entry.QuarantinedPath, entry.OriginalPath, entry.IsDirectory, parent, out var error);
            if (moved == MoveResult.NotMoved)
            {
                skipped++;
                Report(Loc.T($"Restore failed {entry.OriginalPath}: {error}", $"Palautus epäonnistui {entry.OriginalPath}: {error}"));
                continue;
            }

            restored++;
            log.Info(Loc.T($"Restored: {entry.OriginalPath}", $"Palautettu: {entry.OriginalPath}"));
            if (moved == MoveResult.CopiedButSourceRemains)
            {
                Report(Loc.T($"Could not delete the quarantine copy {entry.QuarantinedPath}: {error}", $"Karanteenikopiota ei voitu poistaa {entry.QuarantinedPath}: {error}"));
                continue;
            }

            // Sama rajaus kuin yllä: NNN-kansio poistetaan vain, jos se on session kansion sisällä.
            var slot = FullPathOrEmpty(Path.GetDirectoryName(entry.QuarantinedPath) ?? "");
            if (slot.StartsWith(sessionPrefix, StringComparison.OrdinalIgnoreCase))
                TryDeleteEmptyDirectory(slot);
        }

        return new RestoreResult(restored, skipped, messages);

        static string FullPathOrEmpty(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return ""; }
        }

        void Report(string msg)
        {
            messages.Add(msg);
            log.Warning(msg);
        }
    }
}
