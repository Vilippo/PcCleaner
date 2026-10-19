using Cleaner.Core.Abstractions;
using Cleaner.Core.Models;

namespace Cleaner.Core.Engine;

/// <summary>
/// Kokoaa moduulit yhteen ja tarjoaa käyttöliittymälle yhden paikan,
/// josta skannaus ja puhdistus käynnistetään. Ei tiedä mitään yksittäisistä moduuleista.
/// </summary>
public sealed class CleanerEngine
{
    private readonly Dictionary<string, ICleanerModule> _modules;

    public CleanerEngine(IEnumerable<ICleanerModule> modules)
    {
        _modules = modules.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ICleanerModule> Modules => _modules.Values;

    public ICleanerModule? GetModule(string id) =>
        _modules.TryGetValue(id, out var m) ? m : null;

    /// <summary>Skannaa annetut moduulit (tai kaikki, jos lista on tyhjä).</summary>
    public async Task<IReadOnlyList<ScanResult>> ScanAsync(
        CleanerContext context,
        IEnumerable<string>? moduleIds = null,
        CancellationToken ct = default)
    {
        var targets = ResolveModules(moduleIds);
        var results = new List<ScanResult>();

        foreach (var module in targets)
        {
            ct.ThrowIfCancellationRequested();

            if (module.RequiresAdmin && !context.IsElevated)
                context.Logger.Warning($"Moduuli '{module.Id}' tarvitsee järjestelmänvalvojan oikeudet – osa kohteista voi jäädä löytymättä.");

            context.Logger.Info($"Skannataan: {module.DisplayName}");
            try
            {
                results.Add(await module.ScanAsync(context, ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                context.Logger.Error($"Moduuli '{module.Id}' kaatui skannauksessa: {ex}");
                var failed = new ScanResult { ModuleId = module.Id };
                failed.Errors.Add(ex.Message);
                results.Add(failed);
            }
        }

        return results;
    }

    /// <summary>
    /// Puhdistaa annetut kohteet. Kohteet reititetään oikealle moduulille ModuleId:n perusteella,
    /// joten käyttöliittymä voi antaa yhden sekalaisen listan.
    /// </summary>
    public async Task<IReadOnlyList<CleanResult>> CleanAsync(
        IEnumerable<CleanItem> items,
        CleanerContext context,
        CancellationToken ct = default)
    {
        var results = new List<CleanResult>();

        foreach (var group in items.GroupBy(i => i.ModuleId, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            var module = GetModule(group.Key);
            if (module is null)
            {
                context.Logger.Error($"Tuntematon moduuli '{group.Key}' – {group.Count()} kohdetta ohitettu.");
                continue;
            }

            context.Logger.Info($"{(context.DryRun ? "[KUIVA] " : "")}Puhdistetaan: {module.DisplayName} ({group.Count()} kohdetta)");
            try
            {
                results.Add(await module.CleanAsync(group.ToList(), context, ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                context.Logger.Error($"Moduuli '{module.Id}' kaatui puhdistuksessa: {ex}");
                var failed = new CleanResult { ModuleId = module.Id, WasDryRun = context.DryRun };
                failed.Errors.Add(ex.Message);
                results.Add(failed);
            }
        }

        return results;
    }

    private List<ICleanerModule> ResolveModules(IEnumerable<string>? ids)
    {
        if (ids is null) return _modules.Values.ToList();

        var list = new List<ICleanerModule>();
        foreach (var id in ids)
        {
            var m = GetModule(id) ?? throw new ArgumentException($"Tuntematon moduuli: '{id}'. Käytettävissä: {string.Join(", ", _modules.Keys)}");
            list.Add(m);
        }
        return list.Count == 0 ? _modules.Values.ToList() : list;
    }
}
