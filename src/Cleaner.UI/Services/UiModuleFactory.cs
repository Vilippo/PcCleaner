using Cleaner.Core.Abstractions;
using Cleaner.Core.Logging;
using Cleaner.Modules.DiskUsage;
using Cleaner.Modules.Files;
using Cleaner.Modules.RegistryCleaner;
using Cleaner.Modules.Startup;

namespace Cleaner.UI.Services;

/// <summary>
/// Käyttöliittymän oma moduulikokoonpano (vrt. Cleaner.Cli.ModuleFactory).
/// Ainoa paikka UI:ssa, joka tietää konkreettisista moduuleista.
/// Sääntötiedostot: kaikki säännöt mukaan, myös enabledByDefault: false.
/// UI näyttää ne valintaruutuina, jotka ovat oletuksena pois päältä (ks. ModuleViewModel).
/// </summary>
public static class UiModuleFactory
{
    public static List<ICleanerModule> CreateAll(string rulesDirectory, ICleanerLogger log)
    {
        var modules = new List<ICleanerModule>();

        var filesRules = Path.Combine(rulesDirectory, "files.json");
        if (File.Exists(filesRules))
            modules.Add(FileCleanerModule.FromRuleFile(
                "files", "Tiedostosiivous", "Väliaikaistiedostot, välimuistit, lokit.", filesRules, _ => true));
        else
            log.Warning($"Sääntötiedostoa ei löydy: {filesRules}");

        var browserRules = Path.Combine(rulesDirectory, "browsers.json");
        if (File.Exists(browserRules))
            modules.Add(FileCleanerModule.FromRuleFile(
                "browsers", "Selainten välimuistit", "Edge, Chrome, Firefox, Brave.", browserRules, _ => true));

        modules.Add(new RecycleBinModule());
        modules.Add(new StartupModule());
        modules.Add(new DiskUsageModule());
        modules.Add(new RegistryModule());

        return modules;
    }
}
