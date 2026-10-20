using System.Runtime.Versioning;
using Cleaner.Core.Localization;
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
            Console.WriteLine(Loc.T("Choose: pccleaner uninstall --program <name> [--leftovers-only] [--quiet] [--apply [--all | --item <id>]]",
                                    "Valitse: pccleaner uninstall --program <nimi> [--leftovers-only] [--quiet] [--apply [--all | --item <tunniste>]]"));
            return 0;
        }

        // Valinta: tarkka nimi voittaa, muuten yksikäsitteinen osamerkkijono (kirjainkoolla ei väliä).
        var exact = programs.Where(p => string.Equals(p.Name, options.Program, StringComparison.OrdinalIgnoreCase)).ToList();
        var matches = exact.Count > 0
            ? exact
            : programs.Where(p => p.Name.Contains(options.Program, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0)
        {
            Console.Error.WriteLine(Loc.T($"Program not found: {options.Program}", $"Ohjelmaa ei löydy: {options.Program}"));
            return 2;
        }
        if (matches.Count > 1)
        {
            Console.WriteLine(Loc.T("Multiple matches:", "Useita osumia:"));
            PrintPrograms(matches);
            return 2;
        }

        var program = matches[0];

        // Vaihe 1: poisto-ohjelma. Virhe ei pysäytä: jäänteet skannataan silti.
        if (!options.LeftoversOnly)
        {
            Console.WriteLine(Loc.T($"Starting uninstaller: {program.Name}", $"Käynnistetään poisto-ohjelma: {program.Name}"));
            var run = await module.RunUninstallerAsync(program, options.Quiet, context, CancellationToken.None);
            Console.WriteLine(run.ExitCode is { } code
                ? string.Create(Loc.Culture, $"{Loc.T("  Uninstaller exited with code", "  Poisto-ohjelma päättyi koodilla")} {code} ({run.Elapsed.TotalSeconds:0} s)")
                : Loc.T("  Uninstaller did not start", "  Poisto-ohjelma ei käynnistynyt"));
            Console.WriteLine($"  {Loc.T("Uninstall key removed:", "Uninstall-avain poistui:")} {(run.KeyRemoved ? Loc.T("yes", "kyllä") : Loc.T("no", "ei"))}");
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
            Console.WriteLine($"  {group.Key,-40} {group.Count(),7} {Loc.T("items", "kpl")}   [{group.First().Risk}]");
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
            Console.WriteLine(Loc.T("  (no leftovers)", "  (ei jäänteitä)"));
        else if (!module.TargetKeyRemoved)
            Console.WriteLine(Loc.T("  The program is still installed: nothing is preselected. Use --item <id> or --all if you still want to move items.",
                                    "  Ohjelma on yhä asennettu: mitään ei esivalita. Käytä --item <tunniste> tai --all, jos haluat silti siirtää kohteita."));
        Console.WriteLine();

        if (!options.Apply)
        {
            Console.WriteLine(Loc.T("DRY RUN: nothing is moved. Add --apply to move the preselected (*) items to quarantine, --all to move all, or --item <id>.",
                                "KUIVA AJO: mitään ei siirretä. Lisää --apply siirtääksesi esivalitut (*) karanteeniin, --all kaikki, tai --item <tunniste>."));
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
            Console.WriteLine(Loc.T("Nothing to move.", "Ei mitään siirrettävää."));
            return 0;
        }

        if (!options.Yes)
        {
            var fileCount = items.Count(i => i.Id.StartsWith("uninstall:dir:", StringComparison.Ordinal)
                                          || i.Id.StartsWith("uninstall:file:", StringComparison.Ordinal));
            var registryCount = items.Count - fileCount;
            Console.Write(Loc.T($"Move {fileCount} items to quarantine and remove {registryCount} registry items? [y/N] ",
                                $"Siirretäänkö {fileCount} kohdetta karanteeniin ja poistetaanko {registryCount} rekisterikohdetta? [k/E] "));
            // Molemmilla kielillä hyväksytään y, yes, k ja kyllä; kaikki muu peruuttaa.
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer is not ("k" or "kyllä" or "y" or "yes"))
            {
                Console.WriteLine(Loc.T("Canceled.", "Peruutettu."));
                return 1;
            }
        }

        var realContext = new CleanerContext { DryRun = false, Logger = log, IsElevated = context.IsElevated, Progress = context.Progress };
        var result = await module.CleanAsync(items, realContext);

        foreach (var (item, reason) in result.Skipped.Take(10))
            Console.WriteLine(Loc.T($"  skipped: {item.Path}  ({reason})", $"  ohitettu: {item.Path}  ({reason})"));
        if (result.Skipped.Count > 10)
            Console.WriteLine(Loc.T($"  ... and {result.Skipped.Count - 10} more skipped (see log)",
                                    $"  ... ja {result.Skipped.Count - 10} muuta ohitettua (katso loki)"));
        foreach (var err in result.Errors)
            Console.WriteLine($"  ! {err}");

        Console.WriteLine();
        Console.WriteLine(Loc.T($"Processed: {result.Removed.Count}, skipped {result.Skipped.Count}",
                                $"Käsitelty: {result.Removed.Count}, ohitettu {result.Skipped.Count}"));
        if (module.LastQuarantineDirectory is not null)
            Console.WriteLine($"{Loc.T("Quarantine:", "Karanteeni:")} {module.LastQuarantineDirectory}");
        if (module.LastBackupTimestamp is not null)
            Console.WriteLine($"{Loc.T("Restore:", "Palautus:")} pccleaner restore {module.LastBackupTimestamp}");

        return result.Skipped.Count == items.Count ? 1 : 0;
    }

    private static void PrintPrograms(IReadOnlyList<InstalledProgram> programs)
    {
        Console.WriteLine($"{Loc.T("Name", "Nimi"),-40} {Loc.T("Version", "Versio"),-16} {Loc.T("Publisher", "Julkaisija"),-30} " +
                          $"{Loc.T("Installed", "Asennettu"),-10} {Loc.T("Size", "Koko"),10}  {Loc.T("Key", "Avain")}");
        foreach (var p in programs)
        {
            var size = p.EstimatedSizeBytes > 0 ? ByteFormatter.Format(p.EstimatedSizeBytes) : "";
            var date = p.InstallDate?.ToString("yyyy-MM-dd", Loc.Culture) ?? "";
            Console.WriteLine($"{Fit(p.Name, 40),-40} {Fit(p.Version ?? "", 16),-16} {Fit(p.Publisher ?? "", 30),-30} {date,-10} {size,10}  {p.KeyPath}");
        }
        Console.WriteLine(Loc.T($"({programs.Count} programs)", $"({programs.Count} ohjelmaa)"));
    }

    private static string Fit(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";
}
