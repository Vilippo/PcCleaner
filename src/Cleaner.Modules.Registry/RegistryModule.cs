using System.Runtime.Versioning;
using Cleaner.Core.Abstractions;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Microsoft.Win32;

namespace Cleaner.Modules.RegistryCleaner;

/// <summary>
/// Rajattu rekisterin puhdistus (ARCHITECTURE 6.3). Neljä kategoriaa:
///  - Uninstall-merkinnät, joiden poisto-ohjelmaa ei ole (avain poistetaan),
///  - tiedostotyyppikytkökset: .ext ilman ProgId:tä tai ProgId, jonka open-komennon exe puuttuu (avain poistetaan),
///  - MUICache-arvot, joiden exe puuttuu (arvo poistetaan),
///  - MRU-listat (yksityisyys): avaimen sisältö tyhjennetään, avain säilyy.
///
/// Jokainen löydös kertoo polun, johon merkintä viittaa, ja miksi se katsotaan puuttuvaksi.
/// Ennen jokaista oikeaa muutosta avain viedään .reg-tiedostoon (RegistryBackup); jos vienti
/// epäonnistuu, kohde ohitetaan. RegistryGuard tarkistetaan aina ennen poistoa.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryModule : ICleanerModule
{
    public string Id => "registry";
    public string DisplayName => "Rekisterin puhdistus";
    public string Description => "Orvot Uninstall-merkinnät, tiedostotyyppikytkökset, MUICache ja viimeksi käytettyjen listat. Varmuuskopio ennen jokaista muutosta.";
    public RiskLevel Risk => RiskLevel.High;
    public bool RequiresAdmin => false;
    public bool SelectedByDefault => false;

    public const string UninstallCategory = "Uninstall-merkinnät (orvot)";
    public const string AssociationCategory = "Tiedostotyyppikytkökset (orvot)";
    public const string MuiCacheCategory = "MUICache (orvot)";
    public const string MruCategory = "Viimeksi käytetyt (MRU)";

    private const string IdPrefix = "registry";
    private const char ValueSeparator = '|';

    private static readonly string[] MuiCacheSuffixes = { ".FriendlyAppName", ".ApplicationCompany" };

    public enum ItemKind { Uninstall, Extension, ProgId, MuiCache, Mru }

    private readonly RegistryScanOptions _o;

    /// <summary>Viimeisimmän oikean ajon varmuuskopiokansio, tai null jos mitään ei viety.</summary>
    public string? LastBackupDirectory { get; private set; }

    public RegistryModule() : this(new RegistryScanOptions()) { }
    public RegistryModule(RegistryScanOptions options) => _o = options;

    public RegistryScanOptions Options => _o;

    // ---------- Tunnisteet ----------

    public static string MakeItemId(ItemKind kind, string keyPath, string? valueName = null) =>
        valueName is null ? $"{IdPrefix}:{Token(kind)}:{keyPath}" : $"{IdPrefix}:{Token(kind)}:{keyPath}{ValueSeparator}{valueName}";

    public static bool TryParseItemId(string id, out ItemKind kind, out string keyPath, out string? valueName)
    {
        kind = default; keyPath = ""; valueName = null;
        var parts = id.Split(':', 3);
        if (parts.Length != 3 || parts[0] != IdPrefix) return false;

        kind = parts[1] switch
        {
            "uninstall" => ItemKind.Uninstall,
            "ext" => ItemKind.Extension,
            "progid" => ItemKind.ProgId,
            "muicache" => ItemKind.MuiCache,
            "mru" => ItemKind.Mru,
            _ => (ItemKind)(-1)
        };
        if ((int)kind < 0) return false;

        var rest = parts[2];
        if (kind == ItemKind.MuiCache)
        {
            var sep = rest.IndexOf(ValueSeparator);
            if (sep <= 0 || sep == rest.Length - 1) return false;
            keyPath = rest[..sep];
            valueName = rest[(sep + 1)..];
        }
        else
        {
            keyPath = rest;
        }
        return keyPath.Length > 0;
    }

    private static string Token(ItemKind kind) => kind switch
    {
        ItemKind.Uninstall => "uninstall",
        ItemKind.Extension => "ext",
        ItemKind.ProgId => "progid",
        ItemKind.MuiCache => "muicache",
        ItemKind.Mru => "mru",
        _ => "?"
    };

    // ---------- Skannaus ----------

    public Task<ScanResult> ScanAsync(CleanerContext context, CancellationToken ct = default)
        => Task.Run(() => Scan(context, ct), ct);

    private ScanResult Scan(CleanerContext context, CancellationToken ct)
    {
        var result = new ScanResult { ModuleId = Id };
        var steps = new List<(string Name, Action Run)>();

        if (_o.IncludeUninstall) steps.Add((UninstallCategory, () => ScanUninstall(result, ct)));
        if (_o.IncludeAssociations) steps.Add((AssociationCategory, () => ScanAssociations(result, ct)));
        if (_o.IncludeMuiCache) steps.Add((MuiCacheCategory, () => ScanMuiCache(result, ct)));
        if (_o.IncludeMru) steps.Add((MruCategory, () => ScanMru(result, ct)));

        for (int i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            context.Report((int)(100.0 * i / steps.Count), steps[i].Name);
            try
            {
                steps[i].Run();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var msg = $"{steps[i].Name}: lukeminen epäonnistui: {ex.Message}";
                context.Logger.Warning(msg);
                result.Errors.Add(msg);
            }
        }

        context.Report(100, "Valmis");
        context.Logger.Info($"{DisplayName}: löytyi {result.Count} kohdetta");
        return result;
    }

    private void ScanUninstall(ScanResult result, CancellationToken ct)
    {
        foreach (var root in _o.UninstallKeys)
        {
            using var rootKey = RegistryPaths.OpenReadOnly(root);
            if (rootKey is null) continue;

            foreach (var name in rootKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                using var key = rootKey.OpenSubKey(name, writable: false);
                if (key is null) continue;

                if (key.GetValue("SystemComponent") is int sys && sys == 1) continue;

                var uninstall = key.GetValue("UninstallString") as string;
                if (string.IsNullOrWhiteSpace(uninstall)) continue; // ei tietoa → ei orpo

                var exe = CommandLineParser.ExtractExecutable(uninstall);
                if (exe is null) continue;
                if (CommandLineParser.FileNameLower(exe) == "msiexec.exe") continue; // MSI-tuotteet: ei tarkisteta
                if (!CommandLineParser.IsDefinitelyMissing(exe)) continue;

                var installLocation = key.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation.Trim().Trim('"')))
                    continue; // asennuskansio on yhä olemassa → ei orpo

                var display = key.GetValue("DisplayName") as string ?? name;
                var keyPath = RegistryPaths.Combine(root, name);
                result.Items.Add(new CleanItem(
                    MakeItemId(ItemKind.Uninstall, keyPath), Id, UninstallCategory, keyPath, 0,
                    $"{display}: poisto-ohjelmaa ei ole: {exe}", RiskLevel.Medium));
            }
        }
    }

    private void ScanAssociations(ScanResult result, CancellationToken ct)
    {
        foreach (var root in _o.ClassesKeys)
        {
            using var rootKey = RegistryPaths.OpenReadOnly(root);
            if (rootKey is null) continue;

            foreach (var name in rootKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                if (RegistryGuard.ForbiddenSegments.Any(f => f.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

                var keyPath = RegistryPaths.Combine(root, name);

                if (name.StartsWith('.'))
                {
                    using var extKey = rootKey.OpenSubKey(name, writable: false);
                    if (extKey is null) continue;

                    // Oletusarvo ei ole aina ProgId: jotkin asentajat kirjoittavat siihen selkonimen
                    // ("Microsoft Email Message"). Välilyönnillinen arvo ohitetaan.
                    var progId = (extKey.GetValue("") as string)?.Trim('\0', ' ', '\t');
                    if (string.IsNullOrWhiteSpace(progId) || progId.Contains(' ')) continue;
                    if (ProgIdExists(progId)) continue;

                    // Aliavaimet (OpenWithProgids, PersistentHandler, ShellNew) menetettäisiin poistossa: ohitetaan.
                    if (extKey.SubKeyCount > 0) continue;

                    result.Items.Add(new CleanItem(
                        MakeItemId(ItemKind.Extension, keyPath), Id, AssociationCategory, keyPath, 0,
                        $"Tiedostotyyppi {name} viittaa ProgId:hen '{progId}', jota ei ole.", RiskLevel.High));
                    continue;
                }

                using var commandKey = rootKey.OpenSubKey($@"{name}\shell\open\command", writable: false);
                if (commandKey is null) continue;
                if (commandKey.GetValue("DelegateExecute") is not null) continue; // UWP/COM-ohjattu avaus

                var command = commandKey.GetValue("") as string;
                var exe = CommandLineParser.ExtractExecutable(command);
                if (exe is null || !CommandLineParser.IsDefinitelyMissing(exe)) continue;

                result.Items.Add(new CleanItem(
                    MakeItemId(ItemKind.ProgId, keyPath), Id, AssociationCategory, keyPath, 0,
                    $"ProgId {name}: avauskomennon ohjelmaa ei ole: {exe}", RiskLevel.High));
            }
        }
    }

    private bool ProgIdExists(string progId)
    {
        foreach (var root in _o.ClassesKeys)
        {
            if (RegistryPaths.KeyExists(RegistryPaths.Combine(root, progId))) return true;
        }
        return false;
    }

    private void ScanMuiCache(ScanResult result, CancellationToken ct)
    {
        using var key = RegistryPaths.OpenReadOnly(_o.MuiCacheKey);
        if (key is null) return;

        foreach (var valueName in key.GetValueNames())
        {
            ct.ThrowIfCancellationRequested();
            if (valueName.Length == 0 || valueName.StartsWith('@')) continue;

            var suffix = MuiCacheSuffixes.FirstOrDefault(s => valueName.EndsWith(s, StringComparison.OrdinalIgnoreCase));
            if (suffix is null) continue;

            var exe = valueName[..^suffix.Length];
            if (!CommandLineParser.IsDefinitelyMissing(exe)) continue;

            result.Items.Add(new CleanItem(
                MakeItemId(ItemKind.MuiCache, _o.MuiCacheKey, valueName), Id, MuiCacheCategory, _o.MuiCacheKey, 0,
                $"Arvo '{valueName}': ohjelmaa ei ole: {exe}", RiskLevel.Low));
        }
    }

    private void ScanMru(ScanResult result, CancellationToken ct)
    {
        foreach (var keyPath in _o.MruKeys)
        {
            ct.ThrowIfCancellationRequested();
            using var key = RegistryPaths.OpenReadOnly(keyPath);
            if (key is null) continue;

            var entries = CountEntries(key);
            if (entries == 0) continue;

            var name = keyPath[(keyPath.LastIndexOf('\\') + 1)..];
            result.Items.Add(new CleanItem(
                MakeItemId(ItemKind.Mru, keyPath), Id, MruCategory, keyPath, 0,
                $"{name}: {entries} merkintää. Yksityisyys: lista tyhjennetään, avain säilyy.", RiskLevel.Low));
        }
    }

    private static int CountEntries(RegistryKey key)
    {
        var count = key.GetValueNames().Count(v => v.Length > 0 && v != "MRUListEx" && v != "MRUList");
        foreach (var sub in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(sub, writable: false);
            if (child is not null) count += CountEntries(child);
        }
        return count;
    }

    // ---------- Puhdistus ----------

    public Task<CleanResult> CleanAsync(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct = default)
        => Task.Run(() => Clean(items, context, ct), ct);

    private CleanResult Clean(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct)
    {
        var result = new CleanResult { ModuleId = Id, WasDryRun = context.DryRun };
        var backup = new RegistryBackup(_o.BackupRoot, context.Logger);
        var processed = new List<string>();

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[i];
            context.Report((int)(100.0 * i / items.Count), item.Path);

            if (!string.Equals(item.ModuleId, Id, StringComparison.OrdinalIgnoreCase))
            {
                result.Skipped.Add((item, "Kohde ei kuulu tälle moduulille"));
                continue;
            }

            if (!TryParseItemId(item.Id, out var kind, out var keyPath, out var valueName))
            {
                result.Skipped.Add((item, "Virheellinen tunniste"));
                continue;
            }

            // Vartija on tyyppikohtainen: Uninstall-kohde saa osua vain Uninstall-juurten alle jne.
            // Avainpoistot (Uninstall, .ext, ProgId) sallitaan vain juuren suorille lapsille.
            var exactOnly = kind is ItemKind.Mru or ItemKind.MuiCache;
            if (!RegistryGuard.IsAllowed(keyPath, RootsFor(kind), allowExactRoot: exactOnly, out var reason)
                || (!exactOnly && !IsDirectChildOfAny(keyPath, RootsFor(kind)))
                || (exactOnly && !RootsFor(kind).Any(r => r.Equals(keyPath, StringComparison.OrdinalIgnoreCase))))
            {
                if (reason.Length == 0) reason = "Kohde ei ole sallitun juuren suora lapsi.";
                context.Logger.Warning($"ESTETTY {keyPath}: {reason}");
                result.Skipped.Add((item, reason));
                continue;
            }

            if (RegistryPaths.TryParse(keyPath, out var hive, out _) && hive != RegistryHive.CurrentUser && !context.IsElevated)
            {
                result.Skipped.Add((item, "Tarvitsee järjestelmänvalvojan oikeudet"));
                continue;
            }

            if (!Exists(kind, keyPath, valueName))
            {
                result.Skipped.Add((item, "Kohdetta ei enää ole"));
                continue;
            }

            if (context.DryRun)
            {
                context.Logger.Debug($"[KUIVA] {Describe(kind)}: {keyPath}{(valueName is null ? "" : " / " + valueName)}");
                result.Removed.Add(item);
                continue;
            }

            if (!backup.TryExport(keyPath, out var error))
            {
                context.Logger.Warning($"Ohitettu {keyPath}: {error}");
                result.Skipped.Add((item, $"Varmuuskopio epäonnistui: {error}"));
                continue;
            }

            try
            {
                Apply(kind, keyPath, valueName);
                context.Logger.Info($"{Describe(kind)}: {keyPath}{(valueName is null ? "" : " / " + valueName)}");
                processed.Add(item.Id);
                result.Removed.Add(item);
            }
            catch (UnauthorizedAccessException)
            {
                result.Skipped.Add((item, "Ei oikeuksia"));
            }
            catch (Exception ex)
            {
                result.Skipped.Add((item, ex.Message));
            }
        }

        if (backup.HasFiles)
        {
            backup.WriteManifest(processed);
            LastBackupDirectory = backup.Directory;
            context.Logger.Info($"Rekisterivarmuuskopio: {backup.Directory} (palautus: pccleaner restore {backup.Timestamp})");
        }

        context.Report(100, "Valmis");
        context.Logger.Info($"{DisplayName}: {(context.DryRun ? "käsiteltäisiin" : "käsitelty")} {result.Removed.Count} kohdetta, ohitettu {result.Skipped.Count}");
        return result;
    }

    private IReadOnlyList<string> RootsFor(ItemKind kind) => kind switch
    {
        ItemKind.Uninstall => _o.UninstallKeys,
        ItemKind.Extension or ItemKind.ProgId => _o.ClassesKeys,
        ItemKind.MuiCache => new[] { _o.MuiCacheKey },
        ItemKind.Mru => _o.MruKeys,
        _ => Array.Empty<string>()
    };

    private static bool IsDirectChildOfAny(string keyPath, IEnumerable<string> roots)
    {
        var normalized = keyPath.TrimEnd('\\');
        foreach (var root in roots)
        {
            var prefix = root.TrimEnd('\\') + "\\";
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && !normalized[prefix.Length..].Contains('\\')
                && normalized.Length > prefix.Length)
                return true;
        }
        return false;
    }

    private static string Describe(ItemKind kind) => kind switch
    {
        ItemKind.Uninstall => "Poistettu Uninstall-avain",
        ItemKind.Extension => "Poistettu tiedostotyyppiavain",
        ItemKind.ProgId => "Poistettu ProgId-avain",
        ItemKind.MuiCache => "Poistettu MUICache-arvo",
        ItemKind.Mru => "Tyhjennetty MRU-lista",
        _ => "?"
    };

    private static bool Exists(ItemKind kind, string keyPath, string? valueName)
    {
        using var key = RegistryPaths.OpenReadOnly(keyPath);
        if (key is null) return false;
        if (kind == ItemKind.MuiCache) return valueName is not null && key.GetValue(valueName) is not null;
        if (kind == ItemKind.Mru) return CountEntries(key) > 0;
        return true;
    }

    private static void Apply(ItemKind kind, string keyPath, string? valueName)
    {
        switch (kind)
        {
            case ItemKind.Uninstall:
            case ItemKind.Extension:
            case ItemKind.ProgId:
                DeleteKeyTree(keyPath);
                break;

            case ItemKind.MuiCache:
            {
                using var key = RegistryPaths.OpenWritable(keyPath) ?? throw new IOException("Avainta ei voitu avata kirjoitettavaksi.");
                key.DeleteValue(valueName!, throwOnMissingValue: false);
                break;
            }

            case ItemKind.Mru:
            {
                using var key = RegistryPaths.OpenWritable(keyPath) ?? throw new IOException("Avainta ei voitu avata kirjoitettavaksi.");
                foreach (var v in key.GetValueNames()) key.DeleteValue(v, throwOnMissingValue: false);
                foreach (var s in key.GetSubKeyNames()) key.DeleteSubKeyTree(s, throwOnMissingSubKey: false);
                break;
            }
        }
    }

    private static void DeleteKeyTree(string keyPath)
    {
        if (!RegistryPaths.TryParse(keyPath, out var hive, out var sub) || sub.Length == 0)
            throw new ArgumentException($"Virheellinen polku: {keyPath}");

        var lastSep = sub.LastIndexOf('\\');
        var parentPath = lastSep < 0 ? "" : sub[..lastSep];
        var name = lastSep < 0 ? sub : sub[(lastSep + 1)..];

        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var parent = parentPath.Length == 0 ? baseKey : baseKey.OpenSubKey(parentPath, writable: true);
        if (parent is null) throw new IOException($"Yläavainta ei voitu avata: {parentPath}");
        parent.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
    }
}
