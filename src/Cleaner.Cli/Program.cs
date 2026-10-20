using Cleaner.Cli;
using Cleaner.Core.Engine;
using Cleaner.Core.Logging;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Modules.DiskUsage;
using Cleaner.Modules.RegistryCleaner;
using Cleaner.Modules.Uninstall;

// ---------- Argumenttien käsittely ----------
// Pidetään tarkoituksella yksinkertaisena: ei ulkoisia kirjastoja.

var args_ = args.ToList();
if (args_.Count == 0 || args_.Contains("--help") || args_.Contains("-h"))
{
    PrintHelp();
    return 0;
}

var command = args_[0].ToLowerInvariant();
bool verbose = Flag("--verbose") || Flag("-v");
bool apply = Flag("--apply");
bool yes = Flag("--yes") || Flag("-y");
bool includeDisabled = Flag("--include-disabled");
var moduleIds = Values("--module");
var itemFilters = Values("--item");
var scanPaths = Values("--path");
var topValue = Values("--top").LastOrDefault();
var programValue = Values("--program").LastOrDefault();
bool leftoversOnly = Flag("--leftovers-only");
bool quiet = Flag("--quiet");
bool all = Flag("--all");
var sortValue = Values("--sort").LastOrDefault();
var rulesDir = Values("--rules").FirstOrDefault()
               ?? Path.Combine(AppContext.BaseDirectory, "rules");

// ---------- Lokit ja konteksti ----------
using var fileLog = new FileLogger(minLevel: LogLevel.Debug);
var log = new CompositeLogger(new ConsoleLogger(verbose), fileLog);

var context = new CleanerContext
{
    DryRun = !apply,
    Logger = log,
    IsElevated = ElevationHelper.IsElevated(),
    Progress = verbose ? new Progress<(int Percent, string Message)>(p =>
        Console.Error.Write($"\r  {p.Percent,3}%  {Truncate(p.Message, 60),-60}")) : null
};

// Levytilan analyysin asetukset vain, jos käyttäjä antoi niitä.
var diskUsage = new DiskUsageOptions();
if (scanPaths.Count > 0) diskUsage = diskUsage with { Roots = scanPaths };
if (topValue is not null)
{
    if (!int.TryParse(topValue, out var top) || top < 1)
    {
        Console.Error.WriteLine($"--top vaatii positiivisen kokonaisluvun, sai '{topValue}'.");
        return 2;
    }
    diskUsage = diskUsage with { TopFolders = top, TopFiles = top };
}

var engine = new CleanerEngine(ModuleFactory.CreateAll(rulesDir, log, includeDisabled, diskUsage));

Console.WriteLine($"PC Cleaner  |  {(context.IsElevated ? "järjestelmänvalvoja" : "tavallinen käyttäjä")}  |  loki: {fileLog.FilePath}");
Console.WriteLine();

try
{
    switch (command)
    {
        case "modules":
            foreach (var m in engine.Modules)
                Console.WriteLine($"  {m.Id,-12} {m.DisplayName,-28} riski={m.Risk,-6} admin={(m.RequiresAdmin ? "kyllä" : "ei "),-5} {(m.SelectedByDefault ? "" : "vain --module")}  {m.Description}");
            return 0;

        case "scan":
            await RunScan();
            return 0;

        case "clean":
            return await RunClean();

        case "restore":
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("Rekisterin palautus toimii vain Windowsissa.");
                return 2;
            }
            return RestoreCommand.Run(args_.Skip(1).FirstOrDefault(a => !a.StartsWith("--")), yes, log);

        case "uninstall":
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("Ohjelman poisto toimii vain Windowsissa.");
                return 2;
            }
            if (engine.GetModule("uninstall") is not UninstallModule uninstallModule)
            {
                Console.Error.WriteLine("Uninstall-moduulia ei ole käytettävissä.");
                return 2;
            }
            ProgramSortOrder sort;
            switch (sortValue?.ToLowerInvariant())
            {
                case null: case "name": case "nimi": sort = ProgramSortOrder.Name; break;
                case "date": case "installdate": case "paiva": case "päivä": sort = ProgramSortOrder.InstallDateDescending; break;
                default:
                    Console.Error.WriteLine($"--sort hyväksyy arvot name tai date, sai '{sortValue}'.");
                    return 2;
            }
            var uninstallOptions = new UninstallCommandOptions(programValue, leftoversOnly, quiet, apply, all, yes, itemFilters, sort);
            return await UninstallCommand.RunAsync(uninstallOptions, context, log, uninstallModule);
        }

        default:
            Console.Error.WriteLine($"Tuntematon komento: {command}");
            PrintHelp();
            return 2;
    }
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

// ---------- Komennot ----------

async Task<IReadOnlyList<ScanResult>> RunScan()
{
    // Ilman --module-rajausta ajetaan vain moduulit, jotka sopivat "siivoa kaikki" -ajoon.
    IReadOnlyList<string> targets = moduleIds.Count > 0
        ? moduleIds
        : engine.Modules.Where(m => m.SelectedByDefault).Select(m => m.Id).ToList();
    var optIn = engine.Modules.Where(m => !m.SelectedByDefault && !targets.Contains(m.Id, StringComparer.OrdinalIgnoreCase)).ToList();
    if (optIn.Count > 0)
        Console.WriteLine($"Ohitettu ilman --module-valintaa: {string.Join(", ", optIn.Select(m => m.Id))}");

    var results = await engine.ScanAsync(context, targets);
    if (verbose) Console.Error.WriteLine();

    long total = 0;
    bool anyCleaningModule = false;
    foreach (var r in results)
    {
        var module = engine.GetModule(r.ModuleId)!;
        Console.WriteLine($"== {module.DisplayName} ==");

        foreach (var group in r.Items.GroupBy(i => i.Category).OrderByDescending(g => g.Sum(i => i.SizeBytes)))
        {
            var bytes = group.Sum(i => i.SizeBytes);
            var risk = group.First().Risk;
            // Levytilan analyysin kansiot ovat sisäkkäisiä, joten niiden summa ei tarkoita mitään: ei näytetä.
            var sizeText = module.SelectedByDefault ? ByteFormatter.Format(bytes) : "";
            Console.WriteLine($"  {group.Key,-40} {group.Count(),7} kpl {sizeText,12}   [{risk}]");

            // Vain pyydettäessä ajettavien moduulien (käynnistysohjelmat, levytila) kohteet valitaan yksittäin,
            // joten ne listataan tunnisteineen. Koko näytetään vain, jos sitä on.
            if (!module.SelectedByDefault)
                foreach (var item in group)
                {
                    var size = item.SizeBytes > 0 ? ByteFormatter.Format(item.SizeBytes) + "  " : "";
                    Console.WriteLine($"      {item.Id}\n          {size}{item.Description}");
                }
        }

        foreach (var err in r.Errors)
            Console.WriteLine($"  ! {err}");

        if (r.Count == 0 && r.Errors.Count == 0)
            Console.WriteLine("  (ei löydöksiä)");

        if (module.SelectedByDefault)
        {
            total += r.TotalBytes;
            anyCleaningModule = true;
        }
        Console.WriteLine();
    }

    if (anyCleaningModule)
        Console.WriteLine($"Yhteensä vapautettavissa: {ByteFormatter.Format(total)}");
    return results;
}

async Task<int> RunClean()
{
    var scan = await RunScan();
    var items = scan.SelectMany(r => r.Items).ToList();

    if (itemFilters.Count > 0)
    {
        // --item: tarkka tunniste tai sen osa (kirjainkoolla ei väliä).
        items = items.Where(i => itemFilters.Any(f => i.Id.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
        Console.WriteLine();
        Console.WriteLine($"--item rajasi {items.Count} kohteeseen:");
        foreach (var item in items)
            Console.WriteLine($"  {item.Id}");
    }

    if (items.Count == 0)
    {
        Console.WriteLine("Ei mitään poistettavaa.");
        return 0;
    }

    Console.WriteLine();
    if (context.DryRun)
    {
        Console.WriteLine("KUIVA AJO: mitään ei poisteta. Lisää --apply poistaaksesi oikeasti.");
    }
    else if (!yes)
    {
        Console.Write($"Käsitelläänkö {items.Count} kohdetta ({ByteFormatter.Format(items.Sum(i => i.SizeBytes))})? Tiedostot poistetaan, käynnistysohjelmat poistetaan käytöstä. [k/E] ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (answer is not ("k" or "kyllä" or "y" or "yes"))
        {
            Console.WriteLine("Peruutettu.");
            return 1;
        }
    }

    var results = await engine.CleanAsync(items, context);
    if (verbose) Console.Error.WriteLine();

    long freed = 0;
    int skipped = 0;
    foreach (var r in results)
    {
        freed += r.FreedBytes;
        skipped += r.Skipped.Count;
        foreach (var (item, reason) in r.Skipped.Take(10))
            Console.WriteLine($"  ohitettu: {item.Path}  ({reason})");
        if (r.Skipped.Count > 10)
            Console.WriteLine($"  ... ja {r.Skipped.Count - 10} muuta ohitettua (katso loki)");
    }

    Console.WriteLine();
    Console.WriteLine(context.DryRun
        ? $"Vapautuisi: {ByteFormatter.Format(freed)}, ohitettaisiin {skipped}"
        : $"Vapautettu: {ByteFormatter.Format(freed)}, ohitettu {skipped}");
    return 0;
}

// ---------- Apufunktiot ----------

bool Flag(string name) => args_.Remove(name);

List<string> Values(string name)
{
    var list = new List<string>();
    int idx;
    while ((idx = args_.IndexOf(name)) >= 0 && idx + 1 < args_.Count)
    {
        list.Add(args_[idx + 1]);
        args_.RemoveRange(idx, 2);
    }
    return list;
}

static string Truncate(string s, int max) => s.Length <= max ? s : "..." + s[^(max - 3)..];

static void PrintHelp()
{
    Console.WriteLine("""
        PC Cleaner – komentorivi

        Käyttö:
          pccleaner modules                      Listaa moduulit
          pccleaner scan   [valinnat]            Etsi poistettavat (ei muuta mitään)
          pccleaner clean  [valinnat]            Kuiva ajo: näytä mitä poistettaisiin
          pccleaner clean  --apply [valinnat]    Poista oikeasti (kysyy vahvistuksen)
          pccleaner restore [aikaleima]          Listaa varmuuskopiot (rekisteri + karanteeni) tai palauta yksi
          pccleaner uninstall [--sort date]      Listaa asennetut ohjelmat (oletus nimen mukaan)
          pccleaner uninstall --program <nimi> [--leftovers-only] [--quiet] [--apply] [--all]
                                                 Aja poisto-ohjelma ja siirrä jäänteet karanteeniin

        Valinnat:
          --module <id>     Rajaa moduuliin, voi toistaa (esim. --module files --module browsers)
          --item <tunniste> Rajaa yksittäisiin kohteisiin (tunniste tai sen osa), voi toistaa.
                            Esim. --module startup --item "startup:hkcu-run:Spotify"
          --rules <kansio>  Sääntötiedostojen kansio (oletus: ohjelman vieressä oleva rules\)
          --include-disabled  Ota mukaan myös oletuksena pois päältä olevat säännöt (Prefetch, Windows.old)
          --path <kansio>   Levytilan analyysin juuri, voi toistaa (oletus: kaikki kiinteät asemat)
          --top <n>         Levytilan analyysi: montako suurinta kansiota ja tiedostoa (oletus 20)
          --program <nimi>  uninstall: ohjelman nimi tai sen yksikäsitteinen osa
          --leftovers-only  uninstall: älä aja poisto-ohjelmaa, etsi vain jäänteet
          --quiet           uninstall: käytä hiljaista poistokomentoa, jos ohjelmalla on sellainen
          --all             uninstall --apply: siirrä kaikki jäänteet, ei vain esivalittuja (*)
          --sort <name|date> uninstall: listan järjestys, date = asennuspäivä uusin ensin (päivättömät lopussa)
          --yes, -y         Älä kysy vahvistusta (ajastettuun ajoon)
          --verbose, -v     Näytä edistyminen ja yksityiskohtainen loki

        Moduulit 'startup', 'diskusage', 'registry' ja 'uninstall' ajetaan vain, kun ne annetaan --module-valinnalla.
        'startup' ei poista mitään, vaan poistaa kohteen käytöstä kuten Tehtävienhallinta; palautus sieltä.
        'diskusage' listaa suurimmat kansiot ja tiedostot; --apply poistaa vain --item-valitut tiedostot, ei kansioita.
        'registry' vie jokaisen muutettavan avaimen .reg-tiedostoon ennen muutosta
        (%LOCALAPPDATA%\PcCleaner\registry-backups\<aikaleima>\); palautus: pccleaner restore <aikaleima>.
        'uninstall' ajaa ohjelman oman poisto-ohjelman ja siirtää jäänteet karanteeniin
        (%LOCALAPPDATA%\PcCleaner\quarantine\<aikaleima>\), rekisteri .reg-varmuuskopioon samalla aikaleimalla;
        palautus: pccleaner restore <aikaleima>.

        Kaikki poistot kirjataan tiedostoon %LOCALAPPDATA%\PcCleaner\logs\.
        Järjestelmäkansiot (Windows\Temp, SoftwareDistribution) vaativat järjestelmänvalvojan oikeudet.
        """);
}
