using System.Runtime.Versioning;
using System.Text.Json;
using Cleaner.Core.Abstractions;
using Cleaner.Core.Models;

namespace Cleaner.Modules.Processes;

/// <summary>
/// Prosessit: listaa käyttäjän istunnon prosessit, jotka eivät ole Windows-kansiossa, luokittelee ne ja
/// lopettaa valitut. Suojatut prosessit (oma prosessi, järjestelmä, toinen istunto, Windows-kansio,
/// kriittiset, ilman lopetusoikeutta) jätetään kokonaan pois. Saman exen lapset sulautetaan vanhempaansa.
/// Mitään ei poisteta levyltä eikä rekisteristä; jokainen lopetus kirjataan lokiin nimellä, PID:llä ja polulla.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessesModule : ICleanerModule
{
    public string Id => "processes";
    public string DisplayName => "Prosessit";
    public string Description => "Listaa käyttäjän istunnon taustaprosessit ja lopettaa valitut. Ei poista mitään levyltä.";

    /// <summary>Korkein kohdetaso: "Avoimet ohjelmat" on High. Taustaprosessit ovat Medium.</summary>
    public RiskLevel Risk => RiskLevel.High;
    public bool RequiresAdmin => false;

    /// <summary>Prosessit ovat käyttäjän omia valintoja; niitä ei käsitellä "siivoa kaikki" -ajossa.</summary>
    public bool SelectedByDefault => false;

    private readonly string _rulesFile;
    private readonly IProcessInventory _inventory;
    private readonly IProcessTerminator _terminator;

    /// <summary>Suoritinmittauksen väli (oletus 3 s).</summary>
    public TimeSpan SampleInterval { get; }

    public ProcessesModule(string? rulesFile = null, IProcessInventory? inventory = null, IProcessTerminator? terminator = null, TimeSpan? sampleInterval = null)
    {
        _rulesFile = rulesFile ?? Path.Combine(AppContext.BaseDirectory, "rules", "processes", "known.json");
        _inventory = inventory ?? new ProcessInventory();
        _terminator = terminator ?? new ProcessTerminator();
        SampleInterval = sampleInterval ?? TimeSpan.FromSeconds(3);
    }

    // ---------- Skannaus ----------

    public Task<ScanResult> ScanAsync(CleanerContext context, CancellationToken ct = default)
        => Task.Run(() => Scan(context, ct), ct);

    private ScanResult Scan(CleanerContext context, CancellationToken ct)
    {
        var result = new ScanResult { ModuleId = Id };
        context.Report(0, "Luetaan prosessit");

        var rules = LoadRules(context, result);

        context.Report(10, $"Mitataan suorittimen käyttöä ({SampleInterval.TotalSeconds:0} s)");
        var snapshot = _inventory.Capture(SampleInterval, ct);
        ct.ThrowIfCancellationRequested();

        context.Report(80, "Luokitellaan");
        var groups = ProcessClassifier.Classify(snapshot, rules);
        foreach (var c in groups)
        {
            result.Items.Add(new CleanItem(
                Id: ProcessItemId.Make(c.Root.Pid, c.Root.StartFileTime),
                ModuleId: Id,
                Category: ProcessDescription.CategoryName(c.Category),
                Path: c.Root.ExePath ?? "",
                SizeBytes: c.TotalPrivateBytes,
                Description: ProcessDescription.Build(c, snapshot),
                Risk: c.Risk,
                Preselected: c.Preselected));
        }

        var listed = groups.Sum(g => 1 + g.Children.Count);
        context.Logger.Debug($"{DisplayName}: {snapshot.Processes.Count - listed} prosessia suojattu, ei listata");
        context.Report(100, "Valmis");
        context.Logger.Info($"{DisplayName}: löytyi {result.Count} prosessiryhmää");
        return result;
    }

    private IReadOnlyList<ProcessRule> LoadRules(CleanerContext context, ScanResult result)
    {
        if (!File.Exists(_rulesFile))
        {
            context.Logger.Warning($"Sääntötiedostoa ei löydy: {_rulesFile}");
            return Array.Empty<ProcessRule>();
        }

        try
        {
            return ProcessRuleLoader.LoadFromFile(_rulesFile).Rules;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            var msg = $"Sääntötiedosto on virheellinen: {_rulesFile}: {ex.Message}";
            context.Logger.Warning(msg);
            result.Errors.Add(msg);
            return Array.Empty<ProcessRule>();
        }
    }

    // ---------- Lopetus ----------

    public Task<CleanResult> CleanAsync(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct = default)
        => Task.Run(() => Clean(items, context, ct), ct);

    private CleanResult Clean(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct)
    {
        var result = new CleanResult { ModuleId = Id, WasDryRun = context.DryRun };
        ProcessSnapshot? basic = null; // tuore tieto kerran koko kutsulle, vasta kun ensimmäinen kelvollinen kohde tulee vastaan

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[i];
            context.Report((int)(100.0 * i / items.Count), item.Path);

            if (!string.Equals(item.ModuleId, Id, StringComparison.OrdinalIgnoreCase))
            {
                Skip(result, context, item, "Kohde ei kuulu tälle moduulille");
                continue;
            }

            if (!ProcessItemId.TryParse(item.Id, out var pid, out var start))
            {
                Skip(result, context, item, "Virheellinen tunniste");
                continue;
            }

            basic ??= _inventory.Capture(TimeSpan.Zero, ct);
            var info = basic.Processes.FirstOrDefault(p => p.Pid == pid);
            if (info is null)
            {
                Skip(result, context, item, "Prosessi on jo päättynyt");
                continue;
            }
            if (info.StartFileTime != start)
            {
                Skip(result, context, item, "PID on käytetty uudelleen");
                continue;
            }
            var protectedReason = ProcessGuard.Protects(info, basic);
            if (protectedReason is not null)
            {
                Skip(result, context, item, protectedReason);
                continue;
            }

            if (context.DryRun)
            {
                context.Logger.Debug($"[KUIVA] Lopetettaisiin {info.Name} (PID {pid}) {info.ExePath}");
                result.Removed.Add(item);
                continue;
            }

            if (!_terminator.TryTerminate(pid, start, out var reason))
            {
                Skip(result, context, item, reason);
                continue;
            }

            result.Removed.Add(item);
            context.Logger.Info($"Lopetettiin {info.Name} (PID {pid}): {info.ExePath}");

            // Saman exen jälkeläiset lapsista lapsenlapsiin. Epäonnistuminen ei muuta juuren tulosta.
            // Suojaus tarkistetaan myös jälkeläiselle: sama sääntö kuin juurelle.
            foreach (var child in ProcessClassifier.CollectSameExeDescendants(basic, pid))
            {
                ct.ThrowIfCancellationRequested();
                var childGuard = ProcessGuard.Protects(child, basic);
                if (childGuard is not null)
                {
                    context.Logger.Warning($"Aliprosessia {child.Name} (PID {child.Pid}) ei lopetettu: {childGuard}");
                    continue;
                }
                if (_terminator.TryTerminate(child.Pid, child.StartFileTime, out var childReason))
                    context.Logger.Info($"Lopetettiin aliprosessi {child.Name} (PID {child.Pid}): {child.ExePath}");
                else
                    context.Logger.Warning($"Aliprosessia {child.Name} (PID {child.Pid}) ei lopetettu: {childReason}");
            }
        }

        context.Report(100, "Valmis");
        context.Logger.Info($"{DisplayName}: {(context.DryRun ? "lopetettaisiin" : "lopetettiin")} {result.Removed.Count} prosessia, ohitettu {result.Skipped.Count}");
        return result;
    }

    private static void Skip(CleanResult result, CleanerContext context, CleanItem item, string reason)
    {
        result.Skipped.Add((item, reason));
        context.Logger.Warning($"Ohitettu {item.Path}: {reason}");
    }
}
