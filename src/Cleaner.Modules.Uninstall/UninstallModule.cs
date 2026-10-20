using System.Runtime.Versioning;
using Cleaner.Core.Abstractions;
using Cleaner.Core.Localization;
using Cleaner.Core.Logging;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Microsoft.Win32;

namespace Cleaner.Modules.Uninstall;

/// <summary>
/// Ohjelman poisto: ajaa ohjelman oman poisto-ohjelman (RunUninstallerAsync) ja skannaa jäänteet (ScanAsync).
/// CleanAsync siirtää kansiot ja tiedostot karanteeniin ja vie rekisteriavaimet .reg-tiedostoon ennen
/// poistoa, molemmat samalla aikaleimalla (REQ-6). LeftoverGuard tarkistetaan ennen jokaista siirtoa ja poistoa (REQ-8).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UninstallModule : ICleanerModule
{
    public string Id => "uninstall";
    public string DisplayName => Loc.T("Uninstall programs", "Ohjelman poisto");
    public string Description => Loc.T("Runs the program's own uninstaller and moves leftovers to quarantine.",
        "Ajaa ohjelman oman poisto-ohjelman ja siirtää jäänteet karanteeniin.");
    public RiskLevel Risk => RiskLevel.High;
    public bool RequiresAdmin => true;
    public bool SelectedByDefault => false;

    private readonly LeftoverScanOptions _scanOptions;
    private readonly string _quarantineRoot;
    private readonly string _registryBackupRoot;

    public UninstallModule() : this(new LeftoverScanOptions(), null, null) { }

    public UninstallModule(LeftoverScanOptions scanOptions, string? quarantineRoot, string? registryBackupRoot)
    {
        _scanOptions = scanOptions;
        _quarantineRoot = quarantineRoot ?? Quarantine.DefaultRoot;
        _registryBackupRoot = registryBackupRoot ?? RegistryBackup.DefaultRoot;
    }

    public LeftoverScanOptions Options => _scanOptions;

    /// <summary>Ohjelma, jonka jäänteet ScanAsync skannaa. Null → ScanResult.Errors.</summary>
    public InstalledProgram? Target { get; set; }

    /// <summary>UninstallRunner-tulos: false → Uninstall-avain tarjotaan jäänteenä.</summary>
    public bool TargetKeyRemoved { get; set; } = true;

    /// <summary>Viimeisimmän oikean ajon karanteenikansio, tai null jos mitään ei siirretty.</summary>
    public string? LastQuarantineDirectory { get; private set; }

    /// <summary>Viimeisimmän oikean ajon yhteinen aikaleima (karanteeni ja rekisterivarmuuskopio), tai null.</summary>
    public string? LastBackupTimestamp { get; private set; }

    public IReadOnlyList<InstalledProgram> ListPrograms() => InstalledProgramReader.List(_scanOptions.UninstallRoots);

    /// <summary>Ajaa poisto-ohjelman. Jos asennuskansion alla on käynnissä prosessi, mitään ei käynnistetä (REQ-10).</summary>
    public async Task<UninstallRunResult> RunUninstallerAsync(InstalledProgram program, bool quiet, CleanerContext context, CancellationToken ct)
    {
        var install = LeftoverGuard.InstallLocationOrNull(program.InstallLocation); // levyn juuri tai puuttuva → ei tarkistusta
        if (install is not null && Directory.Exists(install))
        {
            var running = await Task.Run(() => RunningProcessCheck.FindRunningUnder(install), ct);
            if (running is not null)
            {
                var error = Loc.T($"A running process prevents uninstalling: {running}",
                    $"Käynnissä oleva prosessi estää poiston: {running}");
                context.Logger.Warning($"{program.Name}: {error}");
                return new UninstallRunResult(null, false, TimeSpan.Zero, error);
            }
        }

        return await new UninstallRunner(context.Logger).RunAsync(program, quiet, ct);
    }

    // ---------- Skannaus ----------

    public Task<ScanResult> ScanAsync(CleanerContext context, CancellationToken ct = default)
    {
        var target = Target;
        if (target is null)
        {
            var empty = new ScanResult { ModuleId = Id };
            empty.Errors.Add(Loc.T("Select a program first.", "Valitse ohjelma ensin."));
            return Task.FromResult(empty);
        }

        var keyRemoved = TargetKeyRemoved;
        return Task.Run(() =>
        {
            var result = new ScanResult { ModuleId = Id };
            result.Items.AddRange(new LeftoverScanner(_scanOptions).Scan(target, keyRemoved, Id, context.Progress, ct));
            context.Logger.Info(Loc.T($"{DisplayName}: found {result.Count} leftovers for program '{target.Name}'",
                $"{DisplayName}: ohjelmalta '{target.Name}' löytyi {result.Count} jäännöstä"));
            return result;
        }, ct);
    }

    // ---------- Puhdistus ----------

    public Task<CleanResult> CleanAsync(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct = default)
        => Task.Run(() => Clean(items, context, ct), ct);

    private CleanResult Clean(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct)
    {
        var log = context.Logger;
        var result = new CleanResult { ModuleId = Id, WasDryRun = context.DryRun };
        var target = Target;

        // Oikeassa ajossa yhteinen aikaleima: karanteeni saa RegistryBackupin aikaleiman.
        // Kumpikaan ei luo kansiota konstruktorissa; kuivassa ajossa niitä ei luoda lainkaan.
        RegistryBackup? backup = null;
        Quarantine? quarantine = null;
        if (!context.DryRun)
        {
            backup = new RegistryBackup(_registryBackupRoot, log);
            quarantine = new Quarantine(_quarantineRoot, backup.Timestamp, log);
            // Aikaleiman tarkkuus on sekunti: saman sekunnin toinen ajo kirjoittaisi edellisen manifest.txt:n yli.
            for (int attempt = 0; attempt < 3 && (Directory.Exists(backup.Directory) || Directory.Exists(quarantine.Directory)); attempt++)
            {
                WaitForNextSecond();
                backup = new RegistryBackup(_registryBackupRoot, log);
                quarantine = new Quarantine(_quarantineRoot, backup.Timestamp, log);
            }
            LastQuarantineDirectory = null;
            LastBackupTimestamp = null;
        }
        var processed = new List<string>();

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[i];
            context.Report((int)(100.0 * i / items.Count), item.Path);

            // Mikä tahansa kohdekohtainen vaihe voi heittää (esim. ACL-rajattu avain): kohde ohitetaan ja silmukka jatkuu.
            try
            {
                CleanOne(item, context, target, quarantine, backup, result, processed);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log.Warning(Loc.T($"Skipped {item.Path}: {ex.Message}", $"Ohitettu {item.Path}: {ex.Message}"));
                result.Skipped.Add((item, ex.Message));
            }
        }

        if (quarantine is not null && quarantine.HasEntries)
        {
            LastQuarantineDirectory = quarantine.Directory;
            LastBackupTimestamp = quarantine.Timestamp;
            TryWriteManifest(() => quarantine.WriteManifest(processed), result, log);
            log.Info(Loc.T($"Quarantine: {quarantine.Directory} (restore: pccleaner restore {quarantine.Timestamp})",
                $"Karanteeni: {quarantine.Directory} (palautus: pccleaner restore {quarantine.Timestamp})"));
        }
        if (backup is not null && backup.HasFiles)
        {
            LastBackupTimestamp = backup.Timestamp;
            TryWriteManifest(() => backup.WriteManifest(processed), result, log);
            log.Info(Loc.T($"Registry backup: {backup.Directory} (restore: pccleaner restore {backup.Timestamp})",
                $"Rekisterivarmuuskopio: {backup.Directory} (palautus: pccleaner restore {backup.Timestamp})"));
        }

        context.Report(100, Loc.T("Done", "Valmis"));
        log.Info(Loc.T(
            $"{DisplayName}: {(context.DryRun ? "would process" : "processed")} {result.Removed.Count} items, skipped {result.Skipped.Count}",
            $"{DisplayName}: {(context.DryRun ? "käsiteltäisiin" : "käsitelty")} {result.Removed.Count} kohdetta, ohitettu {result.Skipped.Count}"));
        return result;
    }

    /// <summary>Odottaa, kunnes DateTime.Now:n sekunti vaihtuu (enintään noin 1100 ms).</summary>
    private static void WaitForNextSecond()
    {
        var start = DateTime.Now;
        var startSecond = new DateTime(start.Ticks - start.Ticks % TimeSpan.TicksPerSecond);
        for (int waited = 0; waited < 1100; waited += 50)
        {
            var now = DateTime.Now;
            if (now - startSecond >= TimeSpan.FromSeconds(1) || now < startSecond) return;
            Thread.Sleep(50);
        }
    }

    private static void TryWriteManifest(Action write, CleanResult result, ICleanerLogger log)
    {
        try
        {
            write();
        }
        catch (Exception ex)
        {
            var msg = Loc.T($"Failed to write manifest: {ex.Message}", $"Manifestin kirjoitus epäonnistui: {ex.Message}");
            log.Warning(msg);
            result.Errors.Add(msg);
        }
    }

    /// <summary>Yksi kohde: vartija, olemassaolo, oikeudet, REQ-10, kuiva ajo tai siirto/vienti ja poisto.</summary>
    private void CleanOne(CleanItem item, CleanerContext context, InstalledProgram? target, Quarantine? quarantine,
        RegistryBackup? backup, CleanResult result, List<string> processed)
    {
        var log = context.Logger;

        if (!string.Equals(item.ModuleId, Id, StringComparison.OrdinalIgnoreCase))
        {
            result.Skipped.Add((item, Loc.T("Item does not belong to this module", "Kohde ei kuulu tälle moduulille")));
            return;
        }

        if (!LeftoverItemId.TryParse(item.Id, out var kind, out var path, out var valueName))
        {
            result.Skipped.Add((item, Loc.T("Invalid item id", "Virheellinen tunniste")));
            return;
        }

        if (!LeftoverGuard.IsAllowed(kind, path, valueName, target, _scanOptions, out var reason))
        {
            log.Warning(Loc.T($"BLOCKED {path}: {reason}", $"ESTETTY {path}: {reason}"));
            result.Skipped.Add((item, reason));
            return;
        }

        if (!Exists(kind, path, valueName))
        {
            result.Skipped.Add((item, Loc.T("Item no longer exists", "Kohdetta ei enää ole")));
            return;
        }

        if (kind is LeftoverKind.Key or LeftoverKind.Value
            && RegistryPaths.TryParse(path, out var hive, out _) && hive != RegistryHive.CurrentUser && !context.IsElevated)
        {
            result.Skipped.Add((item, Loc.T("Requires administrator rights", "Tarvitsee järjestelmänvalvojan oikeudet")));
            return;
        }

        if (kind is LeftoverKind.Dir or LeftoverKind.File)
        {
            var running = kind == LeftoverKind.Dir
                ? RunningProcessCheck.FindRunningUnder(path)
                : RunningProcessCheck.FindRunningFile(path);
            if (running is not null)
            {
                result.Skipped.Add((item, Loc.T($"A running process prevents moving: {running}",
                    $"Käynnissä oleva prosessi estää siirron: {running}")));
                return;
            }
        }

        if (context.DryRun)
        {
            log.Debug(Loc.T($"[DRY RUN] {Describe(kind)}: {item.Path}", $"[KUIVA] {Describe(kind)}: {item.Path}"));
            result.Removed.Add(item);
            return;
        }

        if (kind is LeftoverKind.Dir or LeftoverKind.File)
        {
            string error;
            var moved = kind == LeftoverKind.Dir
                ? quarantine!.TryMoveDirectory(path, out _, out error)
                : quarantine!.TryMoveFile(path, out _, out error);
            if (!moved)
            {
                result.Skipped.Add((item, error));
                return;
            }
            if (kind == LeftoverKind.Dir) DeleteEmptyParentFolder(path, target, log);
        }
        else
        {
            if (!backup!.TryExport(path, out var error))
            {
                log.Warning(Loc.T($"Skipped {path}: {error}", $"Ohitettu {path}: {error}"));
                result.Skipped.Add((item, Loc.T($"Backup failed: {error}", $"Varmuuskopio epäonnistui: {error}")));
                return;
            }
            try
            {
                if (kind == LeftoverKind.Key)
                {
                    DeleteKeyTree(path);
                    DeleteEmptyParentKey(path, target, log);
                }
                else
                {
                    using var key = RegistryPaths.OpenWritable(path) ?? throw new IOException(Loc.T("Could not open the key for writing.", "Avainta ei voitu avata kirjoitettavaksi."));
                    key.DeleteValue(valueName!, throwOnMissingValue: false);
                }
                log.Info($"{Describe(kind)}: {item.Path}");
            }
            catch (UnauthorizedAccessException)
            {
                result.Skipped.Add((item, Loc.T("Access denied", "Ei oikeuksia")));
                return;
            }
            catch (Exception ex)
            {
                result.Skipped.Add((item, ex.Message));
                return;
            }
        }

        processed.Add(item.Id);
        result.Removed.Add(item);
    }

    private static string Describe(LeftoverKind kind) => kind switch
    {
        LeftoverKind.Dir => Loc.T("Folder to quarantine", "Kansio karanteeniin"),
        LeftoverKind.File => Loc.T("File to quarantine", "Tiedosto karanteeniin"),
        LeftoverKind.Key => Loc.T("Deleted registry key", "Poistettu rekisteriavain"),
        LeftoverKind.Value => Loc.T("Deleted registry value", "Poistettu rekisteriarvo"),
        _ => "?"
    };

    private static bool Exists(LeftoverKind kind, string path, string? valueName)
    {
        switch (kind)
        {
            case LeftoverKind.Dir: return Directory.Exists(path);
            case LeftoverKind.File: return File.Exists(path);
            case LeftoverKind.Key: return RegistryPaths.KeyExists(path);
            case LeftoverKind.Value:
            {
                using var key = RegistryPaths.OpenReadOnly(path);
                // GetValueNames erottaa oletusarvon ("") puuttuvasta arvosta.
                return key is not null && valueName is not null
                    && key.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase);
            }
            default: return false;
        }
    }

    /// <summary>REQ-9: tyhjäksi jäänyt yläkansio pois, jos se ei ole peruskansio ja vartija sallii sen (REQ-8).</summary>
    private void DeleteEmptyParentFolder(string path, InstalledProgram? target, ICleanerLogger log)
    {
        var parent = Path.GetDirectoryName(LeftoverGuard.NormalizePath(path) ?? path);
        if (string.IsNullOrEmpty(parent)) return;

        var isBase = _scanOptions.BaseFolders.Select(b => b.Path).Concat(_scanOptions.ShortcutFolders.Select(s => s.Path))
            .Select(LeftoverGuard.NormalizePath)
            .Any(p => p is not null && p.Equals(parent.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        if (isBase) return;
        if (!LeftoverGuard.IsAllowed(LeftoverKind.Dir, parent, null, target, _scanOptions, out _)) return;

        new SafeFileDeleter(log, dryRun: false).TryDeleteEmptyDirectory(parent);
    }

    /// <summary>
    /// REQ-9: yläavain pois, jos sillä ei ole aliavaimia eikä arvoja, se ei ole RegistryRoots-juuri ja vartija sallii sen (REQ-8).
    /// Ei omaa vientiä: poistetun avaimen .reg-tiedosto luo väliavaimen palautuksessa (reg import).
    /// </summary>
    private void DeleteEmptyParentKey(string keyPath, InstalledProgram? target, ICleanerLogger log)
    {
        var trimmed = keyPath.Trim().TrimEnd('\\');
        var sep = trimmed.LastIndexOf('\\');
        if (sep <= 0) return;
        var parentPath = trimmed[..sep];

        if (_scanOptions.RegistryRoots.Any(r => r.TrimEnd('\\').Equals(parentPath, StringComparison.OrdinalIgnoreCase))) return;
        if (!LeftoverGuard.IsAllowed(LeftoverKind.Key, parentPath, null, target, _scanOptions, out _)) return;

        try
        {
            using (var parent = RegistryPaths.OpenReadOnly(parentPath))
            {
                if (parent is null || parent.SubKeyCount != 0 || parent.ValueCount != 0) return;
            }

            var grandSep = parentPath.LastIndexOf('\\');
            using var grandParent = RegistryPaths.OpenWritable(parentPath[..grandSep]);
            if (grandParent is null) return;
            grandParent.DeleteSubKey(parentPath[(grandSep + 1)..], throwOnMissingSubKey: false);
            log.Info(Loc.T($"Deleted empty parent key: {parentPath}", $"Poistettu tyhjä yläavain: {parentPath}"));
        }
        catch (Exception ex)
        {
            log.Debug(Loc.T($"Skipped deleting parent key {parentPath}: {ex.Message}",
                $"Yläavaimen poisto ohitettu {parentPath}: {ex.Message}"));
        }
    }

    private static void DeleteKeyTree(string keyPath)
    {
        if (!RegistryPaths.TryParse(keyPath, out var hive, out var sub) || sub.Length == 0)
            throw new ArgumentException(Loc.T($"Invalid path: {keyPath}", $"Virheellinen polku: {keyPath}"));

        var lastSep = sub.LastIndexOf('\\');
        var parentPath = lastSep < 0 ? "" : sub[..lastSep];
        var name = lastSep < 0 ? sub : sub[(lastSep + 1)..];

        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var parent = parentPath.Length == 0 ? baseKey : baseKey.OpenSubKey(parentPath, writable: true);
        if (parent is null) throw new IOException(Loc.T($"Could not open parent key: {parentPath}", $"Yläavainta ei voitu avata: {parentPath}"));
        parent.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
    }
}
