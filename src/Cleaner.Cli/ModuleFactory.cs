using System.Runtime.InteropServices;
using Cleaner.Core.Abstractions;
using Cleaner.Core.Logging;
using Cleaner.Core.Rules;
using Cleaner.Modules.DiskUsage;
using Cleaner.Modules.Files;
using Cleaner.Modules.RegistryCleaner;
using Cleaner.Modules.Startup;

namespace Cleaner.Cli;

/// <summary>
/// Kokoaa käytettävissä olevat moduulit. Tämä on AINOA paikka,
/// joka tietää konkreettisista moduuleista. WinUI-sovellus saa oman vastaavan luokan.
/// </summary>
public static class ModuleFactory
{
    /// <param name="includeDisabled">
    /// Ota mukaan myös säännöt, joilla enabledByDefault on epätosi (esim. Prefetch, Windows.old).
    /// </param>
    /// <param name="diskUsage">Levytilan analyysin asetukset (juuret, top-N). Null = oletukset.</param>
    public static List<ICleanerModule> CreateAll(string rulesDirectory, ICleanerLogger log,
        bool includeDisabled = false, DiskUsageOptions? diskUsage = null)
    {
        var modules = new List<ICleanerModule>();
        Func<CleanRule, bool> filter = r => includeDisabled || r.EnabledByDefault;

        var filesRules = Path.Combine(rulesDirectory, "files.json");
        if (File.Exists(filesRules))
            modules.Add(FileCleanerModule.FromRuleFile(
                "files", "Tiedostosiivous", "Väliaikaistiedostot, välimuistit, lokit.", filesRules, filter));
        else
            log.Warning($"Sääntötiedostoa ei löydy: {filesRules}");

        var browserRules = Path.Combine(rulesDirectory, "browsers.json");
        if (File.Exists(browserRules))
            modules.Add(FileCleanerModule.FromRuleFile(
                "browsers", "Selainten välimuistit", "Edge, Chrome, Firefox, Brave.", browserRules, filter));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            modules.Add(new RecycleBinModule());
            modules.Add(new StartupModule());
            modules.Add(new RegistryModule());
        }

        modules.Add(new DiskUsageModule(diskUsage ?? new DiskUsageOptions()));

        return modules;
    }
}
