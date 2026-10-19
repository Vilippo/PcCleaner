using System.Runtime.Versioning;
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
                Console.WriteLine("Ei varmuuskopioita (rekisteri tai karanteeni).");
                return 0;
            }
            Console.WriteLine("Varmuuskopiot (uusin ensin):");
            foreach (var s in sessions)
                Console.WriteLine($"  {s.Timestamp}   {s.Summary}");
            Console.WriteLine();
            Console.WriteLine("Palauta: pccleaner restore <aikaleima>");
            return 0;
        }

        var chosen = sessions.FirstOrDefault(s => s.Timestamp == timestamp);
        if (chosen is null)
        {
            Console.Error.WriteLine($"Varmuuskopiota '{timestamp}' ei löydy. Listaa: pccleaner restore");
            return 2;
        }

        if (!yes)
        {
            Console.Write($"Palautetaanko varmuuskopio {chosen.Timestamp} ({chosen.Summary})? [k/E] ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer is not ("k" or "kyllä" or "y" or "yes"))
            {
                Console.WriteLine("Peruutettu.");
                return 1;
            }
        }

        var result = BackupSessions.Restore(timestamp, log);
        foreach (var msg in result.Messages.Take(10)) Console.WriteLine($"  ! {msg}");
        if (result.Messages.Count > 10)
            Console.WriteLine($"  ... ja {result.Messages.Count - 10} muuta viestiä (katso loki)");
        Console.WriteLine($"Rekisteri: palautettu {result.RegistryImported}, epäonnistui {result.RegistryFailed}. " +
                          $"Karanteeni: palautettu {result.QuarantineRestored}, ohitettu {result.QuarantineSkipped}." +
                          (result.RegistryFailed > 0 ? " HKLM-avaimet vaativat järjestelmänvalvojan." : ""));
        return result.RegistryFailed == 0 && result.QuarantineSkipped == 0 ? 0 : 1;
    }
}
