using Cleaner.Core.Abstractions;
using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Core.Services;

namespace Cleaner.Modules.Files;

/// <summary>
/// Sääntöpohjainen tiedostosiivous: lukee rules/files.json-tiedoston ja
/// etsii/poistaa sen määrittelemät tiedostot. Tämä sama moduuli hoitaa myös
/// selainten välimuistit, koska ne ovat vain tiedostoja eri poluissa (rules/browsers.json).
/// </summary>
public sealed class FileCleanerModule : ICleanerModule
{
    private readonly IReadOnlyList<CleanRule> _rules;

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public RiskLevel Risk => _rules.Count == 0 ? RiskLevel.Low : _rules.Max(r => r.Risk);
    public bool RequiresAdmin => _rules.Any(r => r.RequiresAdmin);

    public IReadOnlyList<CleanRule> Rules => _rules;

    public FileCleanerModule(string id, string displayName, string description, IEnumerable<CleanRule> rules)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        _rules = rules.ToList();
    }

    /// <summary>
    /// Luo moduulin suoraan sääntötiedostosta. Oletuksena mukaan tulevat vain säännöt,
    /// joilla <see cref="CleanRule.EnabledByDefault"/> on tosi; <paramref name="filter"/> korvaa tämän.
    /// </summary>
    public static FileCleanerModule FromRuleFile(string id, string displayName, string description, string ruleFilePath,
        Func<CleanRule, bool>? filter = null)
    {
        var set = RuleLoader.LoadFromFile(ruleFilePath);
        filter ??= r => r.EnabledByDefault;
        return new FileCleanerModule(id, displayName, description, set.Rules.Where(filter));
    }

    public Task<ScanResult> ScanAsync(CleanerContext context, CancellationToken ct = default)
    {
        // Levyoperaatiot ovat synkronisia, joten ajetaan taustasäikeessä
        // ettei käyttöliittymä jäädy.
        return Task.Run(() => Scan(context, ct), ct);
    }

    private ScanResult Scan(CleanerContext context, CancellationToken ct)
    {
        var result = new ScanResult { ModuleId = Id };
        var now = DateTime.UtcNow;

        for (int i = 0; i < _rules.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var rule = _rules[i];
            context.Report((int)(100.0 * i / _rules.Count), rule.Name);

            if (rule.SkipIfProcessRunning.Count > 0 &&
                ProcessChecker.IsAnyRunning(rule.SkipIfProcessRunning, out var running))
            {
                var msg = $"{rule.Name}: ohitettu, koska '{running}' on käynnissä. Sulje ohjelma ja skannaa uudelleen.";
                context.Logger.Warning(msg);
                result.Errors.Add(msg);
                continue;
            }

            if (rule.RequiresAdmin && !context.IsElevated)
                context.Logger.Debug($"{rule.Name}: tarvitsee admin-oikeudet, yritetään silti.");

            foreach (var dir in rule.Paths.SelectMany(PathExpander.Expand).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                ScanDirectory(dir, rule, now, result, context, ct);
            }
        }

        context.Report(100, "Valmis");
        context.Logger.Info($"{DisplayName}: löytyi {result.Count} kohdetta, {ByteFormatter.Format(result.TotalBytes)}");
        return result;
    }

    private void ScanDirectory(string dir, CleanRule rule, DateTime now, ScanResult result, CleanerContext context, CancellationToken ct)
    {
        var patterns = rule.Patterns.Count == 0 ? new List<string> { "*" } : rule.Patterns;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = rule.Recursive,
            IgnoreInaccessible = true,          // ei kaadu oikeusvirheisiin
            AttributesToSkip = FileAttributes.ReparsePoint, // ei seurata symlinkkejä/junctioneita
            ReturnSpecialDirectories = false
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in patterns)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, pattern, options);
            }
            catch (Exception ex)
            {
                var msg = $"{rule.Name}: ei voitu lukea {dir}: {ex.Message}";
                context.Logger.Warning(msg);
                result.Errors.Add(msg);
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!seen.Add(file)) continue; // sama tiedosto useasta patternista

                FileInfo info;
                try { info = new FileInfo(file); }
                catch { continue; }

                if (rule.MinAgeHours > 0)
                {
                    // Käytetään uusinta aikaleimaa, jotta vasta kirjoitettu tiedosto ei lähde.
                    var newest = info.LastWriteTimeUtc > info.CreationTimeUtc ? info.LastWriteTimeUtc : info.CreationTimeUtc;
                    if ((now - newest).TotalHours < rule.MinAgeHours)
                        continue;
                }

                result.Items.Add(new CleanItem(
                    Id: file,
                    ModuleId: Id,
                    Category: rule.Name,
                    Path: file,
                    SizeBytes: info.Length,
                    Description: rule.Description,
                    Risk: rule.Risk));
            }
        }
    }

    public Task<CleanResult> CleanAsync(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct = default)
    {
        return Task.Run(() => Clean(items, context, ct), ct);
    }

    private CleanResult Clean(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct)
    {
        var result = new CleanResult { ModuleId = Id, WasDryRun = context.DryRun };
        var deleter = new SafeFileDeleter(context.Logger, context.DryRun);
        var touchedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[i];

            if (!string.Equals(item.ModuleId, Id, StringComparison.OrdinalIgnoreCase))
            {
                result.Skipped.Add((item, "Kohde ei kuulu tälle moduulille"));
                continue;
            }

            if (i % 50 == 0)
                context.Report((int)(100.0 * i / items.Count), item.Path);

            if (deleter.TryDeleteFile(item.Path, out var reason))
            {
                result.Removed.Add(item);
                var parent = Path.GetDirectoryName(item.Path);
                if (parent is not null) touchedDirs.Add(parent);
            }
            else
            {
                result.Skipped.Add((item, reason));
            }
        }

        // Siivotaan tyhjäksi jääneet alikansiot, syvimmät ensin.
        // Säännön juurikansiota ei poisteta koskaan.
        var roots = _rules.Where(r => r.RemoveEmptyDirs)
                          .SelectMany(r => r.Paths.SelectMany(PathExpander.Expand))
                          .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Mukaan otetaan myös yläkansiot juureen asti (esim. sub/deep → myös sub),
        // koska deep-kansion poiston jälkeen sub voi jäädä tyhjäksi.
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in touchedDirs)
        {
            var current = dir;
            while (current is not null
                   && !roots.Contains(current)
                   && roots.Any(root => current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(current);
                current = Path.GetDirectoryName(current);
            }
        }

        foreach (var dir in candidates.OrderByDescending(d => d.Length))
            deleter.TryDeleteEmptyDirectory(dir);

        context.Report(100, "Valmis");
        context.Logger.Info($"{DisplayName}: {(context.DryRun ? "poistettaisiin" : "poistettiin")} {result.Removed.Count} kohdetta ({ByteFormatter.Format(result.FreedBytes)}), ohitettu {result.Skipped.Count}");
        return result;
    }
}
