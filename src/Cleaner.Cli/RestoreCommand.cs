using System.Runtime.Versioning;
using Cleaner.Core.Localization;
using Cleaner.Core.Logging;
using Cleaner.Core.Services;

namespace Cleaner.Cli;

/// <summary>pccleaner restore [aikaleima]: listaa varmuuskopiot (rekisteri + karanteeni) tai palauttaa yhden.</summary>
[SupportedOSPlatform("windows")]
public static class RestoreCommand
{
    public static int Run(string? timestamp, bool yes, ICleanerLogger log)
    {
        var sessions = BackupSessions.List();

        if (timestamp is null)
        {
            if (sessions.Count == 0)
            {
                Console.WriteLine(Loc.T("No backups (registry or quarantine).", "Ei varmuuskopioita (rekisteri tai karanteeni)."));
                return 0;
            }
            Console.WriteLine(Loc.T("Backups (newest first):", "Varmuuskopiot (uusin ensin):"));
            foreach (var s in sessions)
                Console.WriteLine($"  {s.Timestamp}   {s.Summary}");
            Console.WriteLine();
            Console.WriteLine(Loc.T("Restore: pccleaner restore <timestamp>", "Palauta: pccleaner restore <aikaleima>"));
            return 0;
        }

        var chosen = sessions.FirstOrDefault(s => s.Timestamp == timestamp);
        if (chosen is null)
        {
            Console.Error.WriteLine(Loc.T($"Backup '{timestamp}' not found. List backups: pccleaner restore",
                                          $"Varmuuskopiota '{timestamp}' ei löydy. Listaa: pccleaner restore"));
            return 2;
        }

        if (!yes)
        {
            Console.Write(Loc.T($"Restore backup {chosen.Timestamp} ({chosen.Summary})? [y/N] ",
                                $"Palautetaanko varmuuskopio {chosen.Timestamp} ({chosen.Summary})? [k/E] "));
            // Molemmilla kielillä hyväksytään y, yes, k ja kyllä; kaikki muu peruuttaa.
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer is not ("k" or "kyllä" or "y" or "yes"))
            {
                Console.WriteLine(Loc.T("Canceled.", "Peruutettu."));
                return 1;
            }
        }

        var result = BackupSessions.Restore(timestamp, log);
        foreach (var msg in result.Messages.Take(10)) Console.WriteLine($"  ! {msg}");
        if (result.Messages.Count > 10)
            Console.WriteLine(Loc.T($"  ... and {result.Messages.Count - 10} more messages (see log)",
                                    $"  ... ja {result.Messages.Count - 10} muuta viestiä (katso loki)"));
        Console.WriteLine(Loc.T(
            $"Registry: restored {result.RegistryImported}, failed {result.RegistryFailed}. " +
            $"Quarantine: restored {result.QuarantineRestored}, skipped {result.QuarantineSkipped}." +
            (result.RegistryFailed > 0 ? " HKLM keys require administrator rights." : ""),
            $"Rekisteri: palautettu {result.RegistryImported}, epäonnistui {result.RegistryFailed}. " +
            $"Karanteeni: palautettu {result.QuarantineRestored}, ohitettu {result.QuarantineSkipped}." +
            (result.RegistryFailed > 0 ? " HKLM-avaimet vaativat järjestelmänvalvojan." : "")));
        return result.RegistryFailed == 0 && result.QuarantineSkipped == 0 ? 0 : 1;
    }
}
