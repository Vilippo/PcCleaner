using System.Runtime.Versioning;
using Cleaner.Core.Logging;
using Cleaner.Core.Services;

namespace Cleaner.Cli;

/// <summary>pccleaner restore [aikaleima]: listaa rekisterivarmuuskopiot tai tuo yhden takaisin.</summary>
[SupportedOSPlatform("windows")]
public static class RestoreCommand
{
    public static int Run(string? timestamp, bool yes, ICleanerLogger log)
    {
        var backups = RegistryBackup.List();

        if (timestamp is null)
        {
            if (backups.Count == 0)
            {
                Console.WriteLine($"Ei rekisterivarmuuskopioita kansiossa {RegistryBackup.DefaultRoot}");
                return 0;
            }
            Console.WriteLine("Rekisterivarmuuskopiot (uusin ensin):");
            foreach (var b in backups)
                Console.WriteLine($"  {b.Timestamp}   {b.FileCount} .reg-tiedostoa   {b.Summary}");
            Console.WriteLine();
            Console.WriteLine("Palauta: pccleaner restore <aikaleima>");
            return 0;
        }

        var chosen = backups.FirstOrDefault(b => b.Timestamp == timestamp);
        if (chosen is null)
        {
            Console.Error.WriteLine($"Varmuuskopiota '{timestamp}' ei löydy. Listaa: pccleaner restore");
            return 2;
        }

        if (!yes)
        {
            Console.Write($"Tuodaanko {chosen.FileCount} .reg-tiedostoa takaisin rekisteriin ({chosen.Timestamp})? [k/E] ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer is not ("k" or "kyllä" or "y" or "yes"))
            {
                Console.WriteLine("Peruutettu.");
                return 1;
            }
        }

        var result = RegistryBackup.Restore(timestamp, log);
        foreach (var msg in result.Messages) Console.WriteLine($"  ! {msg}");
        Console.WriteLine($"Palautettu {result.Imported} tiedostoa, epäonnistui {result.Failed}." +
                          (result.Failed > 0 ? " HKLM-avaimet vaativat järjestelmänvalvojan." : ""));
        return result.Failed == 0 ? 0 : 1;
    }
}
