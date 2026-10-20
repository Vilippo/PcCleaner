using System.Runtime.Versioning;
using Cleaner.Core.Models;
using Cleaner.Modules.Uninstall;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Jäännöshaku temp-kansioissa (%TEMP%\pccleaner-tests\&lt;guid&gt;) ja testiavaimessa
/// HKCU\Software\PcCleanerTests\&lt;guid&gt; (CON-8). Ohjelma "Foo Bar", julkaisija "Acme Inc".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LeftoverScannerTests : IDisposable
{
    private const string ModuleId = "uninstall";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _sub = $@"Software\PcCleanerTests\{Guid.NewGuid():N}";

    private string ProgramFiles => Path.Combine(_root, "ProgramFiles");
    private string ProgramData => Path.Combine(_root, "ProgramData");
    private string LocalAppData => Path.Combine(_root, "LocalAppData");
    private string LocalPrograms => Path.Combine(LocalAppData, "Programs");
    private string Roaming => Path.Combine(_root, "Roaming");
    private string StartMenu => Path.Combine(_root, "StartMenu");
    private string Install => Path.Combine(ProgramFiles, "Foo Bar");

    private string RegRoot => @"HKCU\" + _sub + @"\Root";
    private string RegRootWow => RegRoot + @"\WOW6432Node";
    private string RunKey => @"HKCU\" + _sub + @"\RunTest";
    private string UninstallKey => @"HKCU\" + _sub + @"\Uninstall\FooBar";

    public LeftoverScannerTests()
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

    private void SeedFolders()
    {
        Directory.CreateDirectory(Install);
        File.WriteAllBytes(Path.Combine(Install, "foo.exe"), new byte[1234]);
        Directory.CreateDirectory(Path.Combine(ProgramData, "Acme", "Foo Bar"));
        Directory.CreateDirectory(Path.Combine(Roaming, "Foo Bar"));
        Directory.CreateDirectory(Path.Combine(ProgramFiles, "Foo Bar Tools"));
        Directory.CreateDirectory(Path.Combine(ProgramFiles, "Foobar"));
        Directory.CreateDirectory(Path.Combine(ProgramFiles, "Other"));
    }

    private void SeedShortcuts()
    {
        CreateShortcut(Path.Combine(StartMenu, "Foo Bar.lnk"), Path.Combine(Install, "foo.exe"));
        CreateShortcut(Path.Combine(StartMenu, "Muu.lnk"), Path.Combine(ProgramFiles, "Other", "x.exe"));
        Directory.CreateDirectory(Path.Combine(StartMenu, "Foo Bar"));
        CreateShortcut(Path.Combine(StartMenu, "Foo Bar", "Käynnistä.lnk"), Path.Combine(Install, "foo.exe"));
    }

    private static void CreateShortcut(string lnkPath, string target)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic sc = shell.CreateShortcut(lnkPath);
        sc.TargetPath = target;
        sc.Save();
    }

    private void SeedRegistry(string install)
    {
        Create(_sub + @"\Root", ("RootPath", install + @"\x.exe")); // juuren oma arvo ei ole kohde
        Create(_sub + @"\Root\Acme\Foo Bar");
        Create(_sub + @"\Root\Foo Bar");
        Create(_sub + @"\Root\WOW6432Node\Acme\Foo Bar");
        Create(_sub + @"\Root\Other\Thing", ("Path", install + @"\x.exe"));
        Create(_sub + @"\Root\Other\Nope", ("Path", @"C:\Elsewhere\x.exe"));
        Create(_sub + @"\RunTest", ("Foo", $@"""{install}\foo.exe"" -tray"), ("Bar", @"C:\Elsewhere\bar.exe"));
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
        RegistryRoots = new[] { RegRoot, RegRootWow },
        RunKeys = new[] { RunKey },
        AppPathsRoots = new[] { @"HKCU\" + _sub + @"\AppPaths" },
        ClassesRoots = new[] { @"HKCU\" + _sub + @"\Classes" },
        UninstallRoots = new[] { @"HKCU\" + _sub + @"\Uninstall" }
    };

    private InstalledProgram Program(string? installLocation) => new(
        KeyPath: UninstallKey, KeyName: "FooBar", Name: "Foo Bar", Version: "1.0", Publisher: "Acme Inc",
        InstallLocation: installLocation, UninstallString: null, QuietUninstallString: null, DisplayIcon: null,
        EstimatedSizeBytes: 0, IsMsi: false);

    private IReadOnlyList<CleanItem> ScanAll(string? installLocation = null, bool keyRemoved = true, CancellationToken ct = default)
    {
        SeedFolders();
        SeedShortcuts();
        SeedRegistry(Install);
        return new LeftoverScanner(Options()).Scan(Program(installLocation ?? Install), keyRemoved, ModuleId, null, ct);
    }

    private static CleanItem Single(IReadOnlyList<CleanItem> items, string path) =>
        Assert.Single(items, i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    // ---------- Kansiot ----------

    [Fact]
    public void Install_folder_is_medium_and_preselected()
    {
        if (!OperatingSystem.IsWindows()) return;
        var items = ScanAll();

        var item = Single(items, Install);
        Assert.Equal(LeftoverScanner.InstallFolderCategory, item.Category);
        Assert.Equal(RiskLevel.Medium, item.Risk);
        Assert.True(item.Preselected);
        Assert.Equal(LeftoverItemId.MakeDir(Install), item.Id);
        Assert.Equal(1234, item.SizeBytes);
        Assert.Contains("Foo Bar", item.Description);
    }

    [Fact]
    public void Publisher_folder_yields_only_matching_child()
    {
        if (!OperatingSystem.IsWindows()) return;
        var items = ScanAll();

        var item = Single(items, Path.Combine(ProgramData, "Acme", "Foo Bar"));
        Assert.Equal(LeftoverScanner.ProgramFoldersCategory, item.Category);
        Assert.Equal(RiskLevel.Medium, item.Risk);
        Assert.False(item.Preselected);
        Assert.Contains("Acme Inc", item.Description);
        Assert.DoesNotContain(items, i => i.Path.Equals(Path.Combine(ProgramData, "Acme"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void User_data_folder_is_high_and_not_preselected()
    {
        if (!OperatingSystem.IsWindows()) return;
        var items = ScanAll();

        var item = Single(items, Path.Combine(Roaming, "Foo Bar"));
        Assert.Equal(LeftoverScanner.UserDataFoldersCategory, item.Category);
        Assert.Equal(RiskLevel.High, item.Risk);
        Assert.False(item.Preselected);
    }

    [Fact]
    public void Token_match_is_program_folder_not_preselected_and_joined_name_does_not_match()
    {
        if (!OperatingSystem.IsWindows()) return;
        var items = ScanAll();

        var tools = Single(items, Path.Combine(ProgramFiles, "Foo Bar Tools"));
        Assert.Equal(LeftoverScanner.ProgramFoldersCategory, tools.Category);
        Assert.Equal(RiskLevel.Medium, tools.Risk);
        Assert.False(tools.Preselected);
        Assert.Contains("foo bar", tools.Description);

        Assert.DoesNotContain(items, i => i.Path.Equals(Path.Combine(ProgramFiles, "Foobar"), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Path.Equals(Path.Combine(ProgramFiles, "Other"), StringComparison.OrdinalIgnoreCase));
    }

    // ---------- Pikakuvakkeet ----------

    [Fact]
    public void Shortcut_to_install_folder_and_start_menu_folder_are_low_risk()
    {
        if (!OperatingSystem.IsWindows()) return;
        var items = ScanAll();

        var lnk = Single(items, Path.Combine(StartMenu, "Foo Bar.lnk"));
        Assert.Equal(LeftoverScanner.ShortcutsCategory, lnk.Category);
        Assert.Equal(RiskLevel.Low, lnk.Risk);
        Assert.True(lnk.Preselected);
        Assert.Equal(LeftoverItemId.MakeFile(Path.Combine(StartMenu, "Foo Bar.lnk")), lnk.Id);
        Assert.Contains(Install, lnk.Description);

        var folder = Single(items, Path.Combine(StartMenu, "Foo Bar"));
        Assert.Equal(LeftoverScanner.ShortcutsCategory, folder.Category);
        Assert.Equal(LeftoverItemId.MakeDir(Path.Combine(StartMenu, "Foo Bar")), folder.Id);

        // Kansion sisällä oleva pikakuvake ei ole erillinen kohde; vieras pikakuvake ei osu.
        Assert.DoesNotContain(items, i => i.Path.EndsWith("Käynnistä.lnk", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Path.EndsWith("Muu.lnk", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- Rekisteri ----------

    [Fact]
    public void Registry_keys_are_name_keys_under_each_root()
    {
        if (!OperatingSystem.IsWindows()) return;
        var items = ScanAll();

        var keys = items.Where(i => i.Category == LeftoverScanner.RegistryKeysCategory).ToList();
        Assert.Equal(
            new[] { RegRoot + @"\Acme\Foo Bar", RegRoot + @"\Foo Bar", RegRootWow + @"\Acme\Foo Bar" },
            keys.Select(k => k.Path));
        Assert.All(keys, k => Assert.Equal(RiskLevel.High, k.Risk));
        Assert.All(keys, k => Assert.False(k.Preselected));
        Assert.Equal(LeftoverItemId.MakeKey(RegRoot + @"\Foo Bar"), keys[1].Id);
        Assert.DoesNotContain(items, i => i.Path.Equals(RegRoot + @"\Acme", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Registry_values_pointing_to_install_folder_including_run_key()
    {
        if (!OperatingSystem.IsWindows()) return;
        var items = ScanAll();

        var values = items.Where(i => i.Category == LeftoverScanner.RegistryValuesCategory).ToList();
        Assert.Equal(
            new[] { LeftoverItemId.MakeValue(RegRoot + @"\Other\Thing", "Path"), LeftoverItemId.MakeValue(RunKey, "Foo") },
            values.Select(v => v.Id));
        Assert.All(values, v => Assert.Equal(RiskLevel.High, v.Risk));
        Assert.All(values, v => Assert.Contains(Install, v.Description));
    }

    [Fact]
    public void Value_on_root_key_itself_is_not_an_item()
    {
        if (!OperatingSystem.IsWindows()) return;
        var items = ScanAll();

        var values = items.Where(i => i.Category == LeftoverScanner.RegistryValuesCategory).ToList();
        Assert.DoesNotContain(values, v => v.Id.Equals(LeftoverItemId.MakeValue(RegRoot, "RootPath"), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values, v => v.Path.Equals(RegRoot + @"\RootPath", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(values, v => v.Id == LeftoverItemId.MakeValue(RegRoot + @"\Other\Thing", "Path"));
    }

    [Fact]
    public void Values_under_guard_denied_segments_are_not_items()
    {
        if (!OperatingSystem.IsWindows()) return;
        Create(_sub + @"\Root\Clients\Foo", ("Path", Install + @"\x.exe"));
        Create(_sub + @"\Root\RegisteredApplications", ("Path", Install + @"\x.exe"));
        var items = ScanAll();

        var values = items.Where(i => i.Category == LeftoverScanner.RegistryValuesCategory).ToList();
        Assert.DoesNotContain(values, v => v.Id.Contains(@"\Clients", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values, v => v.Id.Contains(@"\RegisteredApplications", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(values, v => v.Id == LeftoverItemId.MakeValue(RegRoot + @"\Other\Thing", "Path"));
    }

    [Fact]
    public void Parent_of_shortcut_folder_as_install_location_is_never_offered()
    {
        if (!OperatingSystem.IsWindows()) return;
        var menus = Path.Combine(_root, "Menus");
        var startMenu = Path.Combine(menus, "StartMenu");
        Directory.CreateDirectory(startMenu);
        SeedFolders();
        var options = Options() with { ShortcutFolders = new[] { new ShortcutFolder(startMenu, IsStartMenu: true) } };

        var items = new LeftoverScanner(options).Scan(Program(menus), keyRemoved: true, ModuleId, null, default);

        Assert.DoesNotContain(items, i => i.Category == LeftoverScanner.InstallFolderCategory);
        Assert.DoesNotContain(items, i => i.Path.Equals(menus, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Uninstall_key_is_reported_only_when_not_removed()
    {
        if (!OperatingSystem.IsWindows()) return;
        var kept = ScanAll(keyRemoved: false);
        var item = Single(kept, UninstallKey);
        Assert.Equal(LeftoverScanner.RegistryKeysCategory, item.Category);

        var removed = new LeftoverScanner(Options()).Scan(Program(Install), keyRemoved: true, ModuleId, null, default);
        Assert.DoesNotContain(removed, i => i.Path.Equals(UninstallKey, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nothing_is_preselected_while_program_is_still_installed()
    {
        if (!OperatingSystem.IsWindows()) return;
        var installed = ScanAll(keyRemoved: false);
        Assert.NotEmpty(installed);
        Assert.All(installed, i => Assert.Equal(false, i.Preselected));
        var install = Single(installed, Install);
        Assert.Contains("yhä asennettu", install.Description);

        var removed = new LeftoverScanner(Options()).Scan(Program(Install), keyRemoved: true, ModuleId, null, default);
        Assert.Contains(removed, i => i.Preselected == true);
        Assert.DoesNotContain("yhä asennettu", Single(removed, Install).Description);
    }

    // ---------- Reunatapaukset ----------

    [Fact]
    public void Missing_install_location_skips_install_and_value_rules_only()
    {
        if (!OperatingSystem.IsWindows()) return;
        var items = ScanAll(installLocation: Path.Combine(_root, "nope"));

        Assert.DoesNotContain(items, i => i.Category == LeftoverScanner.InstallFolderCategory);
        Assert.DoesNotContain(items, i => i.Category == LeftoverScanner.RegistryValuesCategory);

        // Muut säännöt toimivat: asennuskansio löytyy nimiosumana Ohjelmakansioista.
        var byName = Single(items, Install);
        Assert.Equal(LeftoverScanner.ProgramFoldersCategory, byName.Category);
        Assert.Contains(items, i => i.Category == LeftoverScanner.RegistryKeysCategory);
    }

    [Fact]
    public void Base_folder_as_install_location_is_never_offered()
    {
        if (!OperatingSystem.IsWindows()) return;
        var items = ScanAll(installLocation: ProgramFiles);

        Assert.DoesNotContain(items, i => i.Path.Equals(ProgramFiles, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Category == LeftoverScanner.InstallFolderCategory);

        var parent = new LeftoverScanner(Options()).Scan(Program(_root), keyRemoved: true, ModuleId, null, default);
        Assert.DoesNotContain(parent, i => i.Path.Equals(_root, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Install_location_under_name_matched_folder_wins_over_parent()
    {
        if (!OperatingSystem.IsWindows()) return;
        var app = Path.Combine(Install, "app");
        Directory.CreateDirectory(app);
        var items = ScanAll(installLocation: app);

        var underFooBar = items.Where(i =>
            i.Path.Equals(Install, StringComparison.OrdinalIgnoreCase)
            || i.Path.StartsWith(Install + "\\", StringComparison.OrdinalIgnoreCase)).ToList();
        var item = Assert.Single(underFooBar);
        Assert.Equal(app, item.Path);
        Assert.Equal(LeftoverScanner.InstallFolderCategory, item.Category);
    }

    [Fact]
    public void Denied_folder_name_as_install_location_is_never_offered()
    {
        if (!OperatingSystem.IsWindows()) return;
        var common = Path.Combine(ProgramFiles, "Common Files");
        Directory.CreateDirectory(common);
        var items = ScanAll(installLocation: common);

        Assert.DoesNotContain(items, i => i.Category == LeftoverScanner.InstallFolderCategory);
        Assert.DoesNotContain(items, i => i.Path.Equals(common, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Category == LeftoverScanner.RegistryValuesCategory);
    }

    [Fact]
    public void Folder_matched_by_two_rules_is_one_item_and_install_rule_wins()
    {
        if (!OperatingSystem.IsWindows()) return;
        var items = ScanAll();

        var item = Single(items, Install);
        Assert.Equal(LeftoverScanner.InstallFolderCategory, item.Category);
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Cancellation_during_value_scan_propagates()
    {
        if (!OperatingSystem.IsWindows()) return;
        SeedFolders();
        SeedRegistry(Install);
        using var cts = new CancellationTokenSource();
        var reported = new List<string>();
        var sync = new SyncProgress(m =>
        {
            reported.Add(m.Message);
            if (m.Message == "Rekisteriarvot") cts.Cancel();
        });

        Assert.Throws<OperationCanceledException>(() =>
            new LeftoverScanner(Options()).Scan(Program(Install), true, ModuleId, sync, cts.Token));
        Assert.Contains("Rekisteriarvot", reported);
        Assert.DoesNotContain("Valmis", reported);
    }

    [Fact]
    public void Two_scans_give_same_ids_and_every_item_has_description_and_category_risk_is_uniform()
    {
        if (!OperatingSystem.IsWindows()) return;
        var first = ScanAll();
        var second = new LeftoverScanner(Options()).Scan(Program(Install), true, ModuleId, null, default);

        Assert.Equal(first.Select(i => i.Id), second.Select(i => i.Id));
        Assert.All(first, i => Assert.False(string.IsNullOrWhiteSpace(i.Description)));
        Assert.All(first, i => Assert.Equal(ModuleId, i.ModuleId));
        foreach (var group in first.GroupBy(i => i.Category))
            Assert.Single(group.Select(i => i.Risk).Distinct());
        Assert.Equal(6, first.Select(i => i.Category).Distinct().Count());
    }

    [Fact]
    public void Item_ids_round_trip()
    {
        Assert.True(LeftoverItemId.TryParse(LeftoverItemId.MakeDir(@"C:\A\B"), out var kind, out var path, out var value));
        Assert.Equal(LeftoverKind.Dir, kind);
        Assert.Equal(@"C:\A\B", path);
        Assert.Null(value);

        Assert.True(LeftoverItemId.TryParse(LeftoverItemId.MakeValue(@"HKCU\Software\X", "Path"), out kind, out path, out value));
        Assert.Equal(LeftoverKind.Value, kind);
        Assert.Equal(@"HKCU\Software\X", path);
        Assert.Equal("Path", value);

        Assert.False(LeftoverItemId.TryParse("registry:key:HKCU\\X", out _, out _, out _));
        Assert.False(LeftoverItemId.TryParse("uninstall:blob:x", out _, out _, out _));
    }

    /// <summary>Synkroninen IProgress: Progress&lt;T&gt; raportoisi SynchronizationContextin kautta viiveellä.</summary>
    private sealed class SyncProgress : IProgress<(int Percent, string Message)>
    {
        private readonly Action<(int Percent, string Message)> _handler;
        public SyncProgress(Action<(int Percent, string Message)> handler) => _handler = handler;
        public void Report((int Percent, string Message) value) => _handler(value);
    }
}
