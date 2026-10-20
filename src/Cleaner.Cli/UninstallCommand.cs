using System.Runtime.Versioning;
using Cleaner.Core.Logging;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Modules.Uninstall;

namespace Cleaner.Cli;

public sealed record UninstallCommandOptions(string? Program, bool LeftoversOnly, bool Quiet, bool Apply, bool All, bool Yes,
    IReadOnlyList<string> ItemFilters, ProgramSortOrder Sort = ProgramSortOrder.Name);

/// <summary>
/// pccleaner uninstall: listaa asennetut ohjelmat, tai valitulle ohjelmalle aja poisto-ohjelma,
/// skannaa jäänteet ja (--apply) siirrä ne karanteeniin.
/// </summary>
[SupportedOSPlatform("windows")]
public static class UninstallCommand
{
    public static async Task<int> RunAsync(UninstallCommandOptions options, CleanerContext context, ICleanerLogger log, UninstallModule module)
    {
        var programs = InstalledProgramReader.Sort(module.ListPrograms(), options.Sort);

        if (options.Program is null)
        {
            PrintPrograms(programs);
            Console.WriteLine();
            Console.WriteLine("Valitse: pccleaner uninstall --program <nimi> [--leftovers-only] [--quiet] [--apply [--all | --item <tunniste>]]");
            return 0;
        }

        // Valinta: tarkka nimi voittaa, muuten yksikäsitteinen osamerkkijono (kirjainkoolla ei väliä).
        var exact = programs.Where(p => string.Equals(p.Name, options.Program, StringComparison.OrdinalIgnoreCase)).ToList();
        var matches = exact.Count > 0
            ? exact
            : programs.Where(p => p.Name.Contains(options.Program, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"Ohjelmaa ei löydy: {options.Program}");
            return 2;
        }
        if (matches.Count > 1)
        {
            Console.WriteLine("Useita osumia:");
            PrintPrograms(matches);
            return 2;
        }

        var program = matches[0];

        // Vaihe 1: poisto-ohjelma. Virhe ei pysäytä: jäänteet skannataan silti.
        if (!options.LeftoversOnly)
        {
            Console.WriteLine($"Käynnistetään poisto-ohjelma: {program.Name}");
            var run = await module.RunUninstallerAsync(program, options.Quiet, context, CancellationToken.None);
            Console.WriteLine(run.ExitCode is { } code
                ? $"  Poisto-ohjelma päättyi koodilla {code} ({run.Elapsed.TotalSeconds:0} s)"
                : "  Poisto-ohjelma ei käynnistynyt");
            Console.WriteLine($"  Uninstall-avain poistui: {(run.KeyRemoved ? "kyllä" : "ei")}");
            if (run.Error is not null)
                Console.WriteLine($"  ! {run.Error}");
            Console.WriteLine();

            module.Target = program;
            module.TargetKeyRemoved = run.KeyRemoved;
        }
        else
        {
            module.Target = program;
            module.TargetKeyRemoved = !RegistryPaths.KeyExists(program.KeyPath);
        }

        // Vaihe 2: jäänteet, aina kuivana.
        var scanContext = new CleanerContext { DryRun = true, Logger = log, IsElevated = context.IsElevated, Progress = context.Progress };
        var scan = await module.ScanAsync(scanContext);

        Console.WriteLine($"== {module.DisplayName}: {program.Name} ==");
        foreach (var group in scan.Items.GroupBy(i => i.Category).OrderByDescending(g => g.Sum(i => i.SizeBytes)))
        {
            Console.WriteLine($"  {group.Key,-40} {group.Count(),7} kpl   [{group.First().Risk}]");
            foreach (var item in group)
            {
                var marker = item.Preselected == true ? "* " : "  ";
                var size = item.SizeBytes > 0 ? ByteFormatter.Format(item.SizeBytes) + "  " : "";
                Console.WriteLine($"    {marker}{item.Id}\n          {size}{item.Description}");
            }
        }
        foreach (var err in scan.Errors)
            Console.WriteLine($"  ! {err}");
        if (scan.Count == 0)
            Console.WriteLine("  (ei jäänteitä)");
        Console.WriteLine();

        if (!options.Apply)
        {
            Console.WriteLine("KUIVA AJO: mitään ei siirretä. Lisää --apply siirtääksesi esivalitut (*) karanteeniin, --all kaikki, tai --item <tunniste>.");
            return 0;
        }

        // Vaihe 3: valitut kohteet karanteeniin.
        List<CleanItem> items;
        if (options.ItemFilters.Count > 0)
            items = scan.Items.Where(i => options.ItemFilters.Any(f => i.Id.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
        else if (options.All)
            items = scan.Items.ToList();
        else
            items = scan.Items.Where(i => i.Preselected == true).ToList();

        if (items.Count == 0)
        {
            Console.WriteLine("Ei mitään siirrettävää.");
            return 0;
        }

        if (!options.Yes)
        {
            var fileCount = items.Count(i => i.Id.StartsWith("uninstall:dir:", StringComparison.Ordinal)
                                          || i.Id.StartsWith("uninstall:file:", StringComparison.Ordinal));
            var registryCount = items.Count - fileCount;
            Console.Write($"Siirretäänkö {fileCount} kohdetta karanteeniin ja poistetaanko {registryCount} rekisterikohdetta? [k/E] ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer is not ("k" or "kyllä" or "y" or "yes"))
            {
                Console.WriteLine("Peruutettu.");
                return 1;
            }
        }

        var realContext = new CleanerContext { DryRun = false, Logger = log, IsElevated = context.IsElevated, Progress = context.Progress };
        var result = await module.CleanAsync(items, realContext);

        foreach (var (item, reason) in result.Skipped.Take(10))
            Console.WriteLine($"  ohitettu: {item.Path}  ({reason})");
        if (result.Skipped.Count > 10)
            Console.WriteLine($"  ... ja {result.Skipped.Count - 10} muuta ohitettua (katso loki)");
        foreach (var err in result.Errors)
            Console.WriteLine($"  ! {err}");

        Console.WriteLine();
        Console.WriteLine($"Käsitelty: {result.Removed.Count}, ohitettu {result.Skipped.Count}");
        if (module.LastQuarantineDirectory is not null)
            Console.WriteLine($"Karanteeni: {module.LastQuarantineDirectory}");
        if (module.LastBackupTimestamp is not null)
            Console.WriteLine($"Palautus: pccleaner restore {module.LastBackupTimestamp}");

        return result.Skipped.Count == items.Count ? 1 : 0;
    }

    private static void PrintPrograms(IReadOnlyList<InstalledProgram> programs)
    {
        Console.WriteLine($"{"Nimi",-40} {"Versio",-16} {"Julkaisija",-30} {"Asennettu",-10} {"Koko",10}  Avain");
        foreach (var p in programs)
        {
            var size = p.EstimatedSizeBytes > 0 ? ByteFormatter.Format(p.EstimatedSizeBytes) : "";
            var date = p.InstallDate?.ToString("yyyy-MM-dd") ?? "";
            Console.WriteLine($"{Fit(p.Name, 40),-40} {Fit(p.Version ?? "", 16),-16} {Fit(p.Publisher ?? "", 30),-30} {date,-10} {size,10}  {p.KeyPath}");
        }
        Console.WriteLine($"({programs.Count} ohjelmaa)");
    }

    private static string Fit(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";
}
