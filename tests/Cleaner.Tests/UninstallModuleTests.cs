using System.Runtime.Versioning;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Modules.Uninstall;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// UninstallModule temp-kansioissa (%TEMP%\pccleaner-tests\&lt;guid&gt;) ja testiavaimessa
/// HKCU\Software\PcCleanerTests\&lt;guid&gt; (CON-8). Ohjelma "Foo Bar", julkaisija "Acme Inc".
/// Karanteeni- ja rekisterivarmuuskopiojuuret ovat samassa temp-kansiossa.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UninstallModuleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _sub = $@"Software\PcCleanerTests\{Guid.NewGuid():N}";

    private string ProgramFiles => Path.Combine(_root, "ProgramFiles");
    private string ProgramData => Path.Combine(_root, "ProgramData");
    private string LocalAppData => Path.Combine(_root, "LocalAppData");
    private string LocalPrograms => Path.Combine(LocalAppData, "Programs");
    private string Roaming => Path.Combine(_root, "Roaming");
    private string StartMenu => Path.Combine(_root, "StartMenu");
    private string Install => Path.Combine(ProgramFiles, "Foo Bar");
    private string PublisherFolder => Path.Combine(ProgramData, "Acme");
    private string DataFolder => Path.Combine(PublisherFolder, "Foo Bar");
    private string QuarantineRoot => Path.Combine(_root, "Quarantine");
    private string BackupRoot => Path.Combine(_root, "RegBackup");

    private string RegRoot => @"HKCU\" + _sub + @"\Root";
    private string PublisherKey => RegRoot + @"\Acme";
    private string ProgramKey => PublisherKey + @"\Foo Bar";
    private string UninstallKey => @"HKCU\" + _sub + @"\Uninstall\FooBar";

    public UninstallModuleTests()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var dir in new[] { ProgramFiles, ProgramData, LocalAppData, LocalPrograms, Roaming, StartMenu })
            Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
        if (OperatingSystem.IsWindows())
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(_sub, throwOnMissingSubKey: false); } catch { }
            try { Registry.CurrentUser.DeleteSubKey(@"Software\PcCleanerTests", throwOnMissingSubKey: false); } catch { }
        }
    }

    // ---------- Testiaineisto ----------

    private void Seed()
    {
        Directory.CreateDirectory(Install);
        File.WriteAllBytes(Path.Combine(Install, "foo.exe"), new byte[1234]);
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(Path.Combine(DataFolder, "settings.ini"), "x=1");
        Directory.CreateDirectory(Path.Combine(ProgramFiles, "Other"));

        Create(_sub + @"\Root\Acme\Foo Bar", ("Setting", "1"));
        Create(_sub + @"\Root\Other", ("Setting", "2"));
        Create(_sub + @"\Uninstall\FooBar", ("DisplayName", "Foo Bar"));
    }

    private static void Create(string sub, params (string Name, string Value)[] values)
    {
        using var key = Registry.CurrentUser.CreateSubKey(sub);
        foreach (var (name, value) in values) key.SetValue(name, value, RegistryValueKind.String);
    }

    private LeftoverScanOptions Options() => new()
    {
        BaseFolders = new[]
        {
            new BaseFolder(ProgramFiles, BaseFolderKind.ProgramFiles),
            new BaseFolder(ProgramData, BaseFolderKind.ProgramData),
            new BaseFolder(LocalAppData, BaseFolderKind.UserData),
            new BaseFolder(LocalPrograms, BaseFolderKind.LocalPrograms),
            new BaseFolder(Roaming, BaseFolderKind.UserData)
        },
        ShortcutFolders = new[] { new ShortcutFolder(StartMenu, IsStartMenu: true) },
        RegistryRoots = new[] { RegRoot },
        RunKeys = new[] { @"HKCU\" + _sub + @"\RunTest" },
        AppPathsRoots = new[] { @"HKCU\" + _sub + @"\AppPaths" },
        ClassesRoots = new[] { @"HKCU\" + _sub + @"\Classes" },
        UninstallRoots = new[] { @"HKCU\" + _sub + @"\Uninstall" }
    };

    private InstalledProgram Program() => new(
        KeyPath: UninstallKey, KeyName: "FooBar", Name: "Foo Bar", Version: "1.0", Publisher: "Acme Inc",
        InstallLocation: Install, UninstallString: null, QuietUninstallString: null, DisplayIcon: null,
        EstimatedSizeBytes: 0, IsMsi: false);

    private UninstallModule Module(LeftoverScanOptions? options = null) =>
        new(options ?? Options(), QuarantineRoot, BackupRoot) { Target = Program() };

    private static CleanerContext Context(bool dryRun, TestLogger? log = null, bool elevated = false) =>
        new() { DryRun = dryRun, Logger = log ?? new TestLogger(), IsElevated = elevated };

    private const string ElevationReason = "Tarvitsee järjestelmänvalvojan oikeudet";
    private const string KeyGuardReason = "Avain ei ole sallittua muotoa";
    private const string ValueGuardReason = "Arvon avain ei ole sallitun juuren alla";

    private static CleanItem Item(string id, string moduleId = "uninstall") =>
        new(id, moduleId, "Testi", id, 0, "väärennetty", RiskLevel.High);

    private static string ReasonFor(CleanResult result, string id) =>
        Assert.Single(result.Skipped, s => s.Item.Id == id).Reason;

    /// <summary>Levyn ja testiavaimen tila: kaikki polut, avaimet ja arvot.</summary>
    private List<string> Snapshot()
    {
        var list = Directory.Exists(_root)
            ? Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories).ToList()
            : new List<string>();
        using (var key = Registry.CurrentUser.OpenSubKey(_sub))
            if (key is not null) AddKeys(key, _sub, list);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;

        static void AddKeys(RegistryKey key, string path, List<string> sink)
        {
            sink.Add(path);
            foreach (var v in key.GetValueNames()) sink.Add($"{path}|{v}={key.GetValue(v)}");
            foreach (var name in key.GetSubKeyNames())
            {
                using var child = key.OpenSubKey(name);
                if (child is not null) AddKeys(child, $@"{path}\{name}", sink);
            }
        }
    }

    private static bool KeyExists(string hkcuPath) => RegistryPaths.KeyExists(hkcuPath);

    // ---------- Skannaus ----------

    [Fact]
    public async Task Scan_twice_gives_same_ids_and_changes_nothing()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var module = Module();
        var before = Snapshot();

        var first = await module.ScanAsync(Context(dryRun: true));
        var second = await module.ScanAsync(Context(dryRun: true));

        Assert.Empty(first.Errors);
        Assert.Equal(first.Items.Select(i => i.Id), second.Items.Select(i => i.Id));
        Assert.Contains(first.Items, i => i.Id == LeftoverItemId.MakeDir(Install));
        Assert.Contains(first.Items, i => i.Id == LeftoverItemId.MakeDir(DataFolder));
        Assert.Contains(first.Items, i => i.Id == LeftoverItemId.MakeKey(ProgramKey));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Scan_without_target_reports_error()
    {
        if (!OperatingSystem.IsWindows()) return;
        var module = new UninstallModule(Options(), QuarantineRoot, BackupRoot);

        var result = await module.ScanAsync(Context(dryRun: true));

        Assert.Empty(result.Items);
        Assert.Equal("Valitse ohjelma ensin.", Assert.Single(result.Errors));
    }

    // ---------- Puhdistus ----------

    [Fact]
    public async Task Dry_run_fills_removed_and_changes_nothing()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var module = Module();
        var items = (await module.ScanAsync(Context(dryRun: true))).Items;
        var before = Snapshot();

        var result = await module.CleanAsync(items, Context(dryRun: true));

        Assert.True(result.WasDryRun);
        Assert.Empty(result.Skipped);
        Assert.Equal(items.Select(i => i.Id), result.Removed.Select(i => i.Id));
        Assert.Equal(before, Snapshot());
        Assert.False(Directory.Exists(QuarantineRoot));
        Assert.False(Directory.Exists(BackupRoot));
        Assert.Null(module.LastQuarantineDirectory);
        Assert.Null(module.LastBackupTimestamp);
    }

    [Fact]
    public async Task Real_run_quarantines_backs_up_cleans_parents_and_restores()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var module = Module();
        var log = new TestLogger();
        var items = (await module.ScanAsync(Context(dryRun: true))).Items;
        Assert.Equal(3, items.Count);

        var result = await module.CleanAsync(items, Context(dryRun: false, log));

        Assert.Empty(result.Skipped);
        Assert.Equal(3, result.Removed.Count);
        Assert.False(Directory.Exists(Install));
        Assert.False(Directory.Exists(DataFolder));
        Assert.False(KeyExists(ProgramKey));

        // REQ-9: tyhjäksi jäänyt julkaisijakansio ja -avain poistuvat; peruskansio ja juuri säilyvät.
        Assert.False(Directory.Exists(PublisherFolder));
        Assert.False(KeyExists(PublisherKey));
        Assert.True(Directory.Exists(ProgramFiles));
        Assert.True(Directory.Exists(ProgramData));
        Assert.True(KeyExists(RegRoot));
        Assert.True(KeyExists(RegRoot + @"\Other"));

        // Yhteinen aikaleima ja manifestit.
        var ts = module.LastBackupTimestamp;
        Assert.NotNull(ts);
        Assert.Equal(Path.Combine(QuarantineRoot, ts!), module.LastQuarantineDirectory);
        var quarantineManifest = File.ReadAllLines(Path.Combine(QuarantineRoot, ts!, "manifest.txt"));
        Assert.Contains(quarantineManifest, l => l.StartsWith("001\tD\t" + Install + "\t", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(quarantineManifest, l => l.StartsWith("002\tD\t" + DataFolder + "\t", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(QuarantineRoot, ts!, "001", "Foo Bar", "foo.exe")));
        var backupManifest = File.ReadAllLines(Path.Combine(BackupRoot, ts!, "manifest.txt"));
        Assert.Contains(backupManifest, l => l.EndsWith("\t" + ProgramKey, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(log.Entries, e => e.Message.Contains($"pccleaner restore {ts}"));

        // Palautus luo yläkansion ja väliavaimen uudelleen.
        var restore = BackupSessions.Restore(ts!, new TestLogger(), registryRoot: BackupRoot, quarantineRoot: QuarantineRoot);

        Assert.Equal(1, restore.RegistryImported);
        Assert.Equal(0, restore.RegistryFailed);
        Assert.Equal(2, restore.QuarantineRestored);
        Assert.True(File.Exists(Path.Combine(Install, "foo.exe")));
        Assert.True(File.Exists(Path.Combine(DataFolder, "settings.ini")));
        using var key = RegistryPaths.OpenReadOnly(ProgramKey);
        Assert.NotNull(key);
        Assert.Equal("1", key!.GetValue("Setting"));
    }

    [Fact]
    public async Task Forged_ids_are_all_skipped_and_nothing_is_moved()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var outside = Path.Combine(_root, "Outside");
        Directory.CreateDirectory(outside);
        Create(_sub + @"\Root\A\B\C", ("v", "1"));
        Create(_sub + @"\Root\CLSID", ("v", "1"));
        Create(_sub + @"\Root\Acme\CLSID", ("v", "1"));
        var module = Module();
        var before = Snapshot();

        var items = new[]
        {
            Item(LeftoverItemId.MakeDir(outside)),
            Item(LeftoverItemId.MakeDir(ProgramData)),
            Item(LeftoverItemId.MakeDir(_root)),
            Item(@"uninstall:key:HKLM\SOFTWARE\Microsoft\PcCleanerTests-ei-ole"),
            Item(LeftoverItemId.MakeKey(RegRoot + @"\A\B\C")),
            Item(LeftoverItemId.MakeKey(RegRoot + @"\CLSID")),
            Item(LeftoverItemId.MakeKey(RegRoot + @"\Acme\CLSID")),
            Item(LeftoverItemId.MakeDir(Install), moduleId: "files"),
            Item("uninstall:bogus:" + Install),
            Item("uninstall:dir:")
        };

        // Korotettuna HKLM-kohde ei ohitu oikeustarkistuksessa: vartijan on hylättävä se ennen rekisteriin koskemista.
        var result = await module.CleanAsync(items, Context(dryRun: false, elevated: true));

        Assert.Empty(result.Removed);
        Assert.Equal(items.Length, result.Skipped.Count);
        Assert.DoesNotContain(result.Skipped, s => s.Reason == ElevationReason);
        Assert.StartsWith(KeyGuardReason, ReasonFor(result, @"uninstall:key:HKLM\SOFTWARE\Microsoft\PcCleanerTests-ei-ole"));
        Assert.StartsWith(KeyGuardReason, ReasonFor(result, LeftoverItemId.MakeKey(RegRoot + @"\A\B\C")));
        Assert.StartsWith(KeyGuardReason, ReasonFor(result, LeftoverItemId.MakeKey(RegRoot + @"\CLSID")));
        Assert.StartsWith("Peruskansiota ei poisteta", ReasonFor(result, LeftoverItemId.MakeDir(ProgramData)));
        Assert.StartsWith("Polku ei ole peruskansion", ReasonFor(result, LeftoverItemId.MakeDir(outside)));
        Assert.False(Directory.Exists(QuarantineRoot));
        Assert.False(Directory.Exists(BackupRoot));
        Assert.Equal(before, Snapshot());
        Assert.Null(module.LastQuarantineDirectory);
        Assert.Null(module.LastBackupTimestamp);
    }

    // ---------- Vartijan tasot (REQ-8) ----------

    [Fact]
    public async Task Value_guard_rejects_root_level_and_too_deep_values_and_allows_run_key()
    {
        if (!OperatingSystem.IsWindows()) return;
        Create(_sub + @"\Root", ("X", "1"));
        Create(_sub + @"\Root\Other\Thing", ("Path", "x"));
        Create(_sub + @"\Root\D1\D2\D3", ("v", "1"));
        Create(_sub + @"\RunTest", ("Foo", "x"));
        var module = Module(Options() with { MaxRegistryDepth = 2 });

        var rootValue = LeftoverItemId.MakeValue(RegRoot, "X");
        var depth2 = LeftoverItemId.MakeValue(RegRoot + @"\Other\Thing", "Path");
        var depth3 = LeftoverItemId.MakeValue(RegRoot + @"\D1\D2\D3", "v");
        var run = LeftoverItemId.MakeValue(@"HKCU\" + _sub + @"\RunTest", "Foo");

        var result = await module.CleanAsync(new[] { Item(rootValue), Item(depth2), Item(depth3), Item(run) }, Context(dryRun: true));

        Assert.Equal(new[] { depth2, run }, result.Removed.Select(i => i.Id));
        Assert.StartsWith(ValueGuardReason, ReasonFor(result, rootValue));
        Assert.StartsWith(ValueGuardReason, ReasonFor(result, depth3));
    }

    [Fact]
    public async Task Key_guard_allows_direct_uninstall_child_only()
    {
        if (!OperatingSystem.IsWindows()) return;
        Create(_sub + @"\Uninstall\X\Y", ("v", "1"));

        var child = LeftoverItemId.MakeKey(@"HKCU\" + _sub + @"\Uninstall\X");
        var grandChild = LeftoverItemId.MakeKey(@"HKCU\" + _sub + @"\Uninstall\X\Y");

        var result = await Module().CleanAsync(new[] { Item(child), Item(grandChild) }, Context(dryRun: true));

        Assert.Equal(child, Assert.Single(result.Removed).Id);
        Assert.StartsWith(KeyGuardReason, ReasonFor(result, grandChild));
    }

    [Fact]
    public async Task Wow6432Node_segment_is_checked_only_after_the_matched_root()
    {
        if (!OperatingSystem.IsWindows()) return;
        Create(_sub + @"\Root\WOW6432Node\Acme\Foo", ("v", "1"));
        var id = LeftoverItemId.MakeKey(RegRoot + @"\WOW6432Node\Acme\Foo");

        var rejected = await Module().CleanAsync(new[] { Item(id) }, Context(dryRun: true));
        Assert.Empty(rejected.Removed);
        Assert.StartsWith(KeyGuardReason, ReasonFor(rejected, id));

        var withWowRoot = Options() with { RegistryRoots = new[] { RegRoot, RegRoot + @"\WOW6432Node" } };
        var allowed = await Module(withWowRoot).CleanAsync(new[] { Item(id) }, Context(dryRun: true));
        Assert.Empty(allowed.Skipped);
        Assert.Equal(id, Assert.Single(allowed.Removed).Id);
    }

    [Fact]
    public async Task Parent_key_with_values_is_kept_after_child_key_is_removed()
    {
        if (!OperatingSystem.IsWindows()) return;
        Create(_sub + @"\Root\Keep", ("Setting", "1"));
        Create(_sub + @"\Root\Keep\Child", ("v", "1"));
        var module = Module();

        var result = await module.CleanAsync(new[] { Item(LeftoverItemId.MakeKey(RegRoot + @"\Keep\Child")) }, Context(dryRun: false));

        Assert.Single(result.Removed);
        Assert.False(KeyExists(RegRoot + @"\Keep\Child"));
        Assert.True(KeyExists(RegRoot + @"\Keep"));
    }
}
