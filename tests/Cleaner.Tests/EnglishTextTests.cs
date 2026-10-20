using System.Runtime.Versioning;
using Cleaner.Core.Abstractions;
using Cleaner.Core.Localization;
using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Modules.DiskUsage;
using Cleaner.Modules.Files;
using Cleaner.Modules.Processes;
using Cleaner.Modules.RegistryCleaner;
using Cleaner.Modules.Startup;
using Cleaner.Modules.Uninstall;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Todistaa, että jokainen moduuli tuottaa englantia, kun <c>Loc.Current = Language.En</c>: näyttönimi ja
/// vähintään yksi moduulin oma teksti (kategoria, kuvaus, ohitussyy tai lokiviesti). Odotetut arvot ovat
/// moduulien <c>Loc.T</c>-kutsujen ensimmäisiä argumentteja. Mikään testi ei muuta oikeaa järjestelmää:
/// tiedostot ovat kansiossa %TEMP%\pccleaner-tests\&lt;guid&gt;, rekisteri avaimessa
/// HKCU\Software\PcCleanerTests\&lt;guid&gt; ja puhdistukset ovat kuivia ajoja.
/// </summary>
[Collection(TestLanguage.Collection)]
[SupportedOSPlatform("windows")]
public sealed class EnglishTextTests : IDisposable
{
    private const string ForeignReasonEn = "Item does not belong to this module";
    private const string ForeignReasonFi = "Kohde ei kuulu tälle moduulille";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _sub = $@"Software\PcCleanerTests\{Guid.NewGuid():N}";
    private readonly TestLogger _logger = new();

    public EnglishTextTests()
    {
        Directory.CreateDirectory(_root);
        Loc.Current = Language.En;
    }

    public void Dispose()
    {
        Loc.Current = Language.Fi;
        try { Registry.CurrentUser.DeleteSubKeyTree(_sub, throwOnMissingSubKey: false); } catch { }
        try { Registry.CurrentUser.DeleteSubKey(@"Software\PcCleanerTests", throwOnMissingSubKey: false); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private CleanerContext DryRun() => new() { Logger = _logger, DryRun = true };

    private string WriteFile(string relative, int bytes)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    /// <summary>Kohde, jonka ModuleId on vieras: jokainen moduuli ohittaa sen koskematta mihinkään.</summary>
    private async Task AssertForeignItemSkippedInEnglish(ICleanerModule module)
    {
        var item = new CleanItem("other:x", "someothermodule", "Other", Path.Combine(_root, "nothing.bin"), 0, "", RiskLevel.Low);

        var result = await module.CleanAsync(new[] { item }, DryRun());

        Assert.Empty(result.Removed);
        var (_, reason) = Assert.Single(result.Skipped);
        Assert.Equal(ForeignReasonEn, reason);
        Assert.NotEqual(ForeignReasonFi, reason);
    }

    private static string RulesDir => Path.Combine(AppContext.BaseDirectory, "rules");

    // ---------- Tiedostot ----------

    [Fact]
    public async Task FileCleaner_items_use_english_rule_texts_and_byte_units()
    {
        WriteFile("a.tmp", 100);
        var module = new FileCleanerModule("files", new LocalizedText("Temp test", "Testi"), "", new[]
        {
            new CleanRule
            {
                Id = "r", Name = "Temporary files", NameFi = "Väliaikaistiedostot",
                Description = "Safe to delete.", DescriptionFi = "Turvallista poistaa.",
                Paths = new() { _root }, MinAgeHours = 0
            }
        });

        var result = await module.ScanAsync(DryRun());

        Assert.Equal("Temp test", module.DisplayName);
        var item = Assert.Single(result.Items);
        Assert.Equal("Temporary files", item.Category);
        Assert.Equal("Safe to delete.", item.Description);
        Assert.Contains(_logger.Entries, e => e.Message == "Temp test: found 1 items, 100 B");
        await AssertForeignItemSkippedInEnglish(module);
    }

    [Fact]
    public async Task FileCleaner_rule_without_finnish_shows_english_in_both_languages()
    {
        WriteFile("a.tmp", 10);
        var module = new FileCleanerModule("files", "Files", "", new[]
        {
            new CleanRule { Id = "own", Name = "My own rule", Description = "Own description.", Paths = new() { _root }, MinAgeHours = 0 }
        });

        var en = Assert.Single((await module.ScanAsync(DryRun())).Items);
        Loc.Current = Language.Fi;
        var fi = Assert.Single((await module.ScanAsync(DryRun())).Items);

        Assert.Equal("My own rule", en.Category);
        Assert.Equal("Own description.", en.Description);
        Assert.Equal("My own rule", fi.Category);
        Assert.Equal("Own description.", fi.Description);
    }

    [Theory]
    [InlineData("files.json")]
    [InlineData("browsers.json")]
    public void Shipped_file_rules_have_finnish_name_and_description(string fileName)
    {
        var set = RuleLoader.LoadFromFile(Path.Combine(RulesDir, fileName));

        Assert.NotEmpty(set.Rules);
        foreach (var rule in set.Rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.NameFi), $"{fileName}: {rule.Id} is missing nameFi");
            Assert.False(string.IsNullOrWhiteSpace(rule.DescriptionFi), $"{fileName}: {rule.Id} is missing descriptionFi");
            Assert.Equal(rule.Name, rule.DisplayName);
            Assert.Equal(rule.Description, rule.DisplayDescription);
        }
    }

    // ---------- Roskakori ----------

    [Fact]
    public async Task RecycleBin_texts_are_english()
    {
        var module = new RecycleBinModule();
        var item = new CleanItem("recyclebin:all", module.Id, "Recycle Bin", "Recycle Bin (all drives)", 0, "", RiskLevel.Low);

        // Kuiva ajo palaa ennen SHEmptyRecycleBin-kutsua: roskakoria ei tyhjennetä.
        var result = await module.CleanAsync(new[] { item }, DryRun());

        Assert.Equal("Recycle Bin", module.DisplayName);
        Assert.True(result.WasDryRun);
        Assert.Contains(_logger.Entries, e => e.Message == "[DRY RUN] Recycle Bin would be emptied.");
    }

    // ---------- Käynnistysohjelmat ----------

    [Fact]
    public async Task Startup_texts_are_english()
    {
        var module = new StartupModule(Array.Empty<StartupSource>(), includeScheduledTasks: false);

        Assert.Equal("Startup programs", module.DisplayName);
        Assert.Equal("Store apps (StartupTask)", StoreStartupTaskSource.Category);
        await AssertForeignItemSkippedInEnglish(module);
    }

    // ---------- Levytila ----------

    [Fact]
    public async Task DiskUsage_categories_and_byte_units_are_english()
    {
        WriteFile("top.bin", 10);
        WriteFile("a/one.bin", 100);
        var module = new DiskUsageModule(new DiskUsageOptions { Roots = new[] { _root }, TopFolders = 5, TopFiles = 5, MaxDegreeOfParallelism = 1 });

        var result = await module.ScanAsync(DryRun());

        Assert.Equal("Disk usage", module.DisplayName);
        Assert.Equal("Largest folders", DiskUsageModule.FolderCategory);
        Assert.Equal("Folders by own files", DiskUsageModule.OwnFolderCategory);
        Assert.Equal("Largest files", DiskUsageModule.FileCategory);

        var rootItem = Assert.Single(result.Items, i => i.Category == "Folders by own files"
            && string.Equals(i.Path, Path.GetFullPath(_root), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("own files 10 B (1 items), subtree total 110 B", rootItem.Description);
        Assert.All(result.Items.Where(i => i.Category == "Largest files"), i => Assert.StartsWith("Modified ", i.Description));
        await AssertForeignItemSkippedInEnglish(module);
    }

    // ---------- Rekisteri ----------

    [Fact]
    public async Task Registry_mru_item_is_english()
    {
        using (var recent = Registry.CurrentUser.CreateSubKey(_sub + @"\RecentDocs"))
        {
            recent.SetValue("0", new byte[] { 1 });
            recent.SetValue("1", new byte[] { 2 });
            recent.SetValue("MRUListEx", new byte[] { 0 });
        }
        var root = @"HKCU\" + _sub;
        var module = new RegistryModule(new RegistryScanOptions
        {
            UninstallKeys = new[] { root + @"\Uninstall" },
            ClassesKeys = new[] { root + @"\Classes" },
            MuiCacheKey = root + @"\MuiCache",
            MruKeys = new[] { root + @"\RecentDocs" },
            BackupRoot = Path.Combine(_root, "backup"),
            IncludeUninstall = false,
            IncludeAssociations = false,
            IncludeMuiCache = false
        });

        var result = await module.ScanAsync(DryRun());

        Assert.Equal("Registry cleanup", module.DisplayName);
        var item = Assert.Single(result.Items);
        Assert.Equal("Recently used (MRU)", item.Category);
        Assert.Equal("RecentDocs: 2 items. Privacy: the list is cleared, the key is kept.", item.Description);
        await AssertForeignItemSkippedInEnglish(module);
    }

    // ---------- Ohjelman poisto ----------

    [Fact]
    public async Task Uninstall_texts_are_english()
    {
        var module = new UninstallModule(new LeftoverScanOptions(), Path.Combine(_root, "Quarantine"), Path.Combine(_root, "RegBackup"));

        var scan = await module.ScanAsync(DryRun());

        Assert.Equal("Uninstall programs", module.DisplayName);
        Assert.Equal("Select a program first.", Assert.Single(scan.Errors));
        await AssertForeignItemSkippedInEnglish(module);
    }

    // ---------- Prosessit ----------

    [Fact]
    public async Task Processes_texts_are_english()
    {
        // Vieras kohde ohitetaan ennen prosessitietojen lukua tai lopetusta.
        var module = new ProcessesModule(rulesFile: Path.Combine(_root, "none.json"));

        Assert.Equal("Processes", module.DisplayName);
        Assert.Equal("Known background processes", ProcessDescription.CategoryName(ProcessCategory.Known));
        Assert.Equal("Program file missing from disk", ProcessDescription.CategoryName(ProcessCategory.MissingExe));
        await AssertForeignItemSkippedInEnglish(module);
    }

    [Fact]
    public void Shipped_process_rules_have_finnish_description()
    {
        var set = ProcessRuleLoader.LoadFromFile(Path.Combine(RulesDir, "processes", "known.json"));

        Assert.NotEmpty(set.Rules);
        foreach (var rule in set.Rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.DescriptionFi), $"{rule.Id} is missing descriptionFi");
            var en = rule.DisplayDescription;
            Loc.Current = Language.Fi;
            var fi = rule.DisplayDescription;
            Loc.Current = Language.En;

            Assert.Equal(rule.Description, en);
            Assert.NotEqual(en, fi);
        }
    }
}
