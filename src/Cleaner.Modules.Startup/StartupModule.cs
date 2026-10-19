using System.Runtime.Versioning;
using Cleaner.Core.Abstractions;
using Cleaner.Core.Models;
using Microsoft.Win32;

namespace Cleaner.Modules.Startup;

/// <summary>
/// Käynnistysohjelmat: Run-avaimet, käynnistyskansiot ja kirjautumistriggeriset ajoitetut tehtävät.
/// Toimenpide on käytöstä poisto, ei poisto: rekisteri- ja kansiokohteet merkitään Explorerin
/// StartupApproved-avaimeen täsmälleen kuten Tehtävienhallinta tekee, joten käyttäjä voi palauttaa
/// ne sieltä. Ajoitetut tehtävät saavat Enabled = false. Mitään ei poisteta levyltä eikä rekisteristä.
///
/// Skannaus palauttaa vain käytössä olevat kohteet, koska vain niille on jotain tehtävää.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupModule : ICleanerModule
{
    public string Id => "startup";
    public string DisplayName => "Käynnistysohjelmat";
    public string Description => "Poistaa käytöstä kirjautuessa käynnistyvät ohjelmat. Ei poista mitään.";
    public RiskLevel Risk => RiskLevel.Medium;
    public bool RequiresAdmin => false;

    /// <summary>Käynnistysohjelmat ovat käyttäjän omia valintoja; niitä ei käsitellä "siivoa kaikki" -ajossa.</summary>
    public bool SelectedByDefault => false;

    private const string IdPrefix = "startup";
    private const string TaskSourceKey = "task";
    private const string TaskCategory = "Ajoitetut tehtävät (kirjautuminen)";

    private readonly Dictionary<string, StartupSource> _sources;
    private readonly bool _includeScheduledTasks;

    public StartupModule() : this(StartupSource.Defaults(), includeScheduledTasks: true) { }

    /// <summary>Testattavuutta varten: omat lähteet ja mahdollisuus jättää Task Scheduler pois.</summary>
    public StartupModule(IEnumerable<StartupSource> sources, bool includeScheduledTasks)
    {
        _sources = sources.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
        _includeScheduledTasks = includeScheduledTasks;
    }

    public IReadOnlyCollection<StartupSource> Sources => _sources.Values;

    // ---------- Skannaus ----------

    public Task<ScanResult> ScanAsync(CleanerContext context, CancellationToken ct = default)
        => Task.Run(() => Scan(context, ct), ct);

    private ScanResult Scan(CleanerContext context, CancellationToken ct)
    {
        var result = new ScanResult { ModuleId = Id };
        int step = 0;
        int total = _sources.Count + (_includeScheduledTasks ? 1 : 0);

        foreach (var source in _sources.Values)
        {
            ct.ThrowIfCancellationRequested();
            context.Report((int)(100.0 * step++ / total), source.DisplayName);

            try
            {
                var entries = source.Kind == StartupSourceKind.RegistryRun ? ReadRunKey(source) : ReadFolder(source);
                foreach (var entry in entries)
                {
                    if (IsDisabled(source, entry.Name))
                    {
                        context.Logger.Debug($"{source.DisplayName}: '{entry.Name}' on jo pois käytöstä.");
                        continue;
                    }

                    result.Items.Add(new CleanItem(
                        Id: MakeItemId(source.Key, entry.Name),
                        ModuleId: Id,
                        Category: source.DisplayName,
                        Path: entry.Location,
                        SizeBytes: 0,
                        Description: entry.Command,
                        Risk: RiskLevel.Medium));
                }
            }
            catch (Exception ex)
            {
                var msg = $"{source.DisplayName}: lukeminen epäonnistui: {ex.Message}";
                context.Logger.Warning(msg);
                result.Errors.Add(msg);
            }
        }

        if (_includeScheduledTasks)
        {
            ct.ThrowIfCancellationRequested();
            context.Report((int)(100.0 * step / total), TaskCategory);

            try
            {
                foreach (var task in TaskSchedulerSource.ListLogonTasks())
                {
                    result.Items.Add(new CleanItem(
                        Id: MakeItemId(TaskSourceKey, task.Path),
                        ModuleId: Id,
                        Category: TaskCategory,
                        Path: task.Path,
                        SizeBytes: 0,
                        Description: task.Command,
                        Risk: RiskLevel.Medium));
                }
            }
            catch (Exception ex)
            {
                var msg = $"{TaskCategory}: lukeminen epäonnistui: {ex.Message}";
                context.Logger.Warning(msg);
                result.Errors.Add(msg);
            }
        }

        context.Report(100, "Valmis");
        context.Logger.Info($"{DisplayName}: löytyi {result.Count} käytössä olevaa kohdetta");
        return result;
    }

    private sealed record StartupEntry(string Name, string Command, string Location);

    private static IEnumerable<StartupEntry> ReadRunKey(StartupSource source)
    {
        using var baseKey = RegistryKey.OpenBaseKey(source.Hive, RegistryView.Default);
        using var key = baseKey.OpenSubKey(source.Location, writable: false);
        if (key is null) yield break;

        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue; // oletusarvo (Default) ei ole käynnistyskohde

            var kind = key.GetValueKind(name);
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) continue;

            var command = key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
            yield return new StartupEntry(name, command, $@"{source.HiveLabel}\{source.Location}\{name}");
        }
    }

    private static IEnumerable<StartupEntry> ReadFolder(StartupSource source)
    {
        if (!Directory.Exists(source.Location)) yield break;

        foreach (var file in Directory.EnumerateFiles(source.Location))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            yield return new StartupEntry(name, ShortcutResolver.Describe(file), file);
        }
    }

    private static bool IsDisabled(StartupSource source, string name)
    {
        using var baseKey = RegistryKey.OpenBaseKey(source.Hive, RegistryView.Default);
        using var key = baseKey.OpenSubKey(source.ApprovedSubKey, writable: false);
        return StartupApproved.IsDisabled(key?.GetValue(name) as byte[]);
    }

    private static bool EntryExists(StartupSource source, string name)
    {
        if (source.Kind == StartupSourceKind.Folder)
            return File.Exists(Path.Combine(source.Location, name));

        using var baseKey = RegistryKey.OpenBaseKey(source.Hive, RegistryView.Default);
        using var key = baseKey.OpenSubKey(source.Location, writable: false);
        return key?.GetValue(name) is not null;
    }

    // ---------- Käytöstä poisto ----------

    public Task<CleanResult> CleanAsync(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct = default)
        => Task.Run(() => Clean(items, context, ct), ct);

    private CleanResult Clean(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct)
    {
        var result = new CleanResult { ModuleId = Id, WasDryRun = context.DryRun };
        var now = DateTime.UtcNow;

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

            if (!TryParseItemId(item.Id, out var sourceKey, out var name))
            {
                result.Skipped.Add((item, "Virheellinen tunniste"));
                continue;
            }

            var ok = sourceKey.Equals(TaskSourceKey, StringComparison.OrdinalIgnoreCase)
                ? TryDisableTask(name, context, out var reason)
                : TryDisableEntry(sourceKey, name, now, context, out reason);

            if (ok) result.Removed.Add(item);
            else
            {
                result.Skipped.Add((item, reason));
                context.Logger.Warning($"Ohitettu {item.Path}: {reason}");
            }
        }

        context.Report(100, "Valmis");
        context.Logger.Info($"{DisplayName}: {(context.DryRun ? "poistettaisiin käytöstä" : "poistettiin käytöstä")} {result.Removed.Count} kohdetta, ohitettu {result.Skipped.Count}");
        return result;
    }

    private bool TryDisableEntry(string sourceKey, string name, DateTime now, CleanerContext context, out string reason)
    {
        if (!_sources.TryGetValue(sourceKey, out var source))
        {
            reason = $"Tuntematon lähde '{sourceKey}'";
            return false;
        }

        if (source.RequiresAdmin && !context.IsElevated)
        {
            reason = "Tarvitsee järjestelmänvalvojan oikeudet";
            return false;
        }

        if (!EntryExists(source, name))
        {
            reason = "Kohdetta ei enää ole";
            return false;
        }

        if (context.DryRun)
        {
            context.Logger.Debug($"[KUIVA] Poistettaisiin käytöstä: {source.HiveLabel}\\{source.ApprovedSubKey}\\{name}");
            reason = "";
            return true;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(source.Hive, RegistryView.Default);
            using var key = baseKey.CreateSubKey(source.ApprovedSubKey, writable: true);
            key.SetValue(name, StartupApproved.BuildDisabledValue(now), RegistryValueKind.Binary);
            context.Logger.Info($"Poistettu käytöstä: {source.DisplayName} / {name}");
            reason = "";
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            reason = "Ei oikeuksia";
        }
        catch (Exception ex)
        {
            reason = ex.Message;
        }
        return false;
    }

    private static bool TryDisableTask(string taskPath, CleanerContext context, out string reason)
    {
        if (!TaskSchedulerSource.Exists(taskPath))
        {
            reason = "Tehtävää ei enää ole";
            return false;
        }

        if (context.DryRun)
        {
            context.Logger.Debug($"[KUIVA] Poistettaisiin käytöstä ajoitettu tehtävä: {taskPath}");
            reason = "";
            return true;
        }

        try
        {
            TaskSchedulerSource.Disable(taskPath);
            context.Logger.Info($"Poistettu käytöstä ajoitettu tehtävä: {taskPath}");
            reason = "";
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            reason = "Ei oikeuksia (tehtävä vaatii järjestelmänvalvojan)";
        }
        catch (Exception ex)
        {
            reason = ex.Message;
        }
        return false;
    }

    // ---------- Tunnisteet ----------

    /// <summary>Muoto: "startup:&lt;lähde&gt;:&lt;nimi&gt;". Nimi saa sisältää kaksoispisteitä.</summary>
    public static string MakeItemId(string sourceKey, string name) => $"{IdPrefix}:{sourceKey}:{name}";

    public static bool TryParseItemId(string id, out string sourceKey, out string name)
    {
        var parts = id.Split(':', 3);
        if (parts.Length == 3 && parts[0] == IdPrefix && parts[1].Length > 0 && parts[2].Length > 0)
        {
            sourceKey = parts[1];
            name = parts[2];
            return true;
        }
        sourceKey = "";
        name = "";
        return false;
    }
}
