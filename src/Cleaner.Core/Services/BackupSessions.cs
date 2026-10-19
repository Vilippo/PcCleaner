using System.Runtime.Versioning;
using Cleaner.Core.Logging;

namespace Cleaner.Core.Services;

/// <summary>
/// Yhdistää rekisterivarmuuskopiot ja karanteenin aikaleimoittain: yksi puhdistusajo = yksi sessio,
/// jolla voi olla .reg-tiedostoja, karanteenikohteita tai molempia.
/// </summary>
[SupportedOSPlatform("windows")]
public static class BackupSessions
{
    public sealed record Session(string Timestamp, int RegistryFiles, int QuarantineEntries, string Summary);
    public sealed record RestoreResult(int RegistryImported, int RegistryFailed, int QuarantineRestored, int QuarantineSkipped, IReadOnlyList<string> Messages);

    /// <summary>Unioni rekisteri- ja karanteenisessioiden aikaleimoista, uusin ensin.</summary>
    public static IReadOnlyList<Session> List(string? registryRoot = null, string? quarantineRoot = null)
    {
        var registry = RegistryBackup.List(registryRoot).ToDictionary(b => b.Timestamp, StringComparer.OrdinalIgnoreCase);
        var quarantine = Quarantine.List(quarantineRoot).ToDictionary(q => q.Timestamp, StringComparer.OrdinalIgnoreCase);

        return registry.Keys.Union(quarantine.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ts => ts, StringComparer.Ordinal)
            .Select(ts =>
            {
                var files = registry.TryGetValue(ts, out var b) ? b.FileCount : 0;
                var entries = quarantine.TryGetValue(ts, out var q) ? q.EntryCount : 0;
                return new Session(ts, files, entries, Summarize(files, entries));
            })
            .ToList();
    }

    /// <summary>Palauttaa session molemmat puolet. Puuttuva puoli ei ole virhe.</summary>
    public static RestoreResult Restore(string timestamp, ICleanerLogger log, string? registryRoot = null, string? quarantineRoot = null)
    {
        var hasRegistry = System.IO.Directory.Exists(Path.Combine(registryRoot ?? RegistryBackup.DefaultRoot, timestamp));
        var hasQuarantine = System.IO.Directory.Exists(Path.Combine(quarantineRoot ?? Quarantine.DefaultRoot, timestamp));

        if (!hasRegistry && !hasQuarantine)
            return new RestoreResult(0, 0, 0, 0, new[] { $"Varmuuskopiota ei ole: {timestamp}" });

        var messages = new List<string>();
        int imported = 0, failed = 0, restored = 0, skipped = 0;

        if (hasRegistry)
        {
            var r = RegistryBackup.Restore(timestamp, log, registryRoot);
            imported = r.Imported;
            failed = r.Failed;
            messages.AddRange(r.Messages);
        }

        if (hasQuarantine)
        {
            var q = Quarantine.Restore(timestamp, log, quarantineRoot);
            restored = q.Restored;
            skipped = q.Skipped;
            messages.AddRange(q.Messages);
        }

        return new RestoreResult(imported, failed, restored, skipped, messages);
    }

    private static string Summarize(int registryFiles, int quarantineEntries)
    {
        var parts = new List<string>();
        if (registryFiles > 0) parts.Add($"{registryFiles} .reg-tiedostoa");
        if (quarantineEntries > 0) parts.Add($"{quarantineEntries} karanteenikohdetta");
        return string.Join(", ", parts);
    }
}
