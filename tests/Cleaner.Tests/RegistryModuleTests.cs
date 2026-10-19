using System.Runtime.Versioning;
using Cleaner.Core.Models;
using Cleaner.Modules.RegistryCleaner;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Tests;

public sealed class CommandLineParserTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\X\\unins000.exe\" /SILENT", "C:\\Program Files\\X\\unins000.exe")]
    [InlineData("C:\\Program Files\\X\\unins000.exe /SILENT", "C:\\Program Files\\X\\unins000.exe")]
    [InlineData("MsiExec.exe /X{GUID}", "MsiExec.exe")]
    [InlineData("C:\\Tools\\app.exe", "C:\\Tools\\app.exe")]
    [InlineData("  \"C:\\A\\b.exe\"  \"%1\"", "C:\\A\\b.exe")]
    public void Extracts_executable(string command, string expected) =>
        Assert.Equal(expected, CommandLineParser.ExtractExecutable(command));

    [Fact]
    public void Expands_environment_variables()
    {
        var result = CommandLineParser.ExtractExecutable("%SystemRoot%\\system32\\notepad.exe %1");
        Assert.NotNull(result);
        Assert.EndsWith("\\system32\\notepad.exe", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"%1\"")]
    [InlineData("%UNKNOWN_VAR_XYZ%\\app.exe")]
    public void Unparseable_returns_null(string? command) =>
        Assert.Null(CommandLineParser.ExtractExecutable(command));

    [Fact]
    public void Missing_detection_is_conservative()
    {
        Assert.False(CommandLineParser.IsDefinitelyMissing("notepad.exe"));                 // suhteellinen
        Assert.False(CommandLineParser.IsDefinitelyMissing(@"\\server\share\x.exe"));        // verkko
        Assert.False(CommandLineParser.IsDefinitelyMissing(@"Q:\nope\x.exe"));               // asemaa ei ole
        Assert.False(CommandLineParser.IsDefinitelyMissing(@"C:\Program Files\WindowsApps\X\x.exe"));
        if (OperatingSystem.IsWindows())
        {
            Assert.False(CommandLineParser.IsDefinitelyMissing(Path.Combine(Environment.SystemDirectory, "cmd.exe")));
            Assert.True(CommandLineParser.IsDefinitelyMissing(@"C:\pccleaner-no-such-dir\x.exe"));
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class RegistryGuardTests
{
    private static readonly string[] Roots = { @"HKCU\Software\Test\Uninstall", @"HKCU\Software\Test\Mru" };

    [Fact]
    public void Allows_child_of_root() =>
        Assert.True(RegistryGuard.IsAllowed(@"HKCU\Software\Test\Uninstall\App", Roots, false, out _));

    [Fact]
    public void Rejects_root_itself_unless_exact_allowed()
    {
        Assert.False(RegistryGuard.IsAllowed(@"HKCU\Software\Test\Mru", Roots, false, out _));
        Assert.True(RegistryGuard.IsAllowed(@"HKCU\Software\Test\Mru", Roots, true, out _));
    }

    [Theory]
    [InlineData(@"HKCU\Software\Test\Uninstall\CLSID\x")]
    [InlineData(@"HKCU\Software\Test\Uninstall\Services")]
    [InlineData(@"HKCU\Software\Test\Uninstall\x\TypeLib")]
    public void Rejects_forbidden_segments(string path)
    {
        Assert.False(RegistryGuard.IsAllowed(path, Roots, true, out var reason));
        Assert.Contains("Suojattu", reason);
    }

    [Theory]
    [InlineData(@"HKCU\Software\Other\App")]
    [InlineData(@"HKLM\Software\Test\Uninstall\App")]
    [InlineData(@"HKCU")]
    [InlineData("garbage")]
    public void Rejects_outside_roots(string path) =>
        Assert.False(RegistryGuard.IsAllowed(path, Roots, true, out _));
}

/// <summary>
/// Testaa moduulia oikealla rekisterillä omassa avaimessa HKCU\Software\PcCleanerTests\<guid>
/// ja varmuuskopiot omaan väliaikaiskansioon. reg.exe export/import toimii HKCU:ssa ilman adminia.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryModuleTests : IDisposable
{
    private readonly string _sub = $@"Software\PcCleanerTests\{Guid.NewGuid():N}";
    private readonly string _backupDir = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));

    private string Root => @"HKCU\" + _sub;
    private string Uninstall => Root + @"\Uninstall";
    private string Classes => Root + @"\Classes";
    private string MuiCache => Root + @"\MuiCache";
    private string RecentDocs => Root + @"\RecentDocs";

    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private const string MissingExe = @"C:\pccleaner-no-such-dir\gone.exe";

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(_sub, throwOnMissingSubKey: false); } catch { }
            try { Registry.CurrentUser.DeleteSubKey(@"Software\PcCleanerTests", throwOnMissingSubKey: false); } catch { }
        }
        try { Directory.Delete(_backupDir, recursive: true); } catch { }
    }

    private RegistryModule CreateModule() => new(new RegistryScanOptions
    {
        UninstallKeys = new[] { Uninstall },
        ClassesKeys = new[] { Classes },
        MuiCacheKey = MuiCache,
        MruKeys = new[] { RecentDocs },
        BackupRoot = _backupDir
    });

    private void Seed()
    {
        using var root = Registry.CurrentUser.CreateSubKey(_sub);

        using (var k = root.CreateSubKey(@"Uninstall\Orphan"))
        {
            k.SetValue("DisplayName", "Orphan App");
            k.SetValue("UninstallString", $"\"{MissingExe}\" /S");
        }
        using (var k = root.CreateSubKey(@"Uninstall\Alive"))
        {
            k.SetValue("DisplayName", "Alive App");
            k.SetValue("UninstallString", $"\"{Cmd}\" /c echo");
        }
        using (var k = root.CreateSubKey(@"Uninstall\Msi"))
        {
            k.SetValue("UninstallString", "MsiExec.exe /X{00000000-0000-0000-0000-000000000000}");
        }
        using (var k = root.CreateSubKey(@"Uninstall\Sys"))
        {
            k.SetValue("UninstallString", $"\"{MissingExe}\"");
            k.SetValue("SystemComponent", 1, RegistryValueKind.DWord);
        }
        using (var k = root.CreateSubKey(@"Uninstall\DirStillThere"))
        {
            k.SetValue("UninstallString", $"\"{MissingExe}\"");
            k.SetValue("InstallLocation", Environment.SystemDirectory);
        }

        using (var k = root.CreateSubKey(@"Classes\.foo")) k.SetValue("", "FooFile");
        using (var k = root.CreateSubKey(@"Classes\FooFile\shell\open\command")) k.SetValue("", $"\"{MissingExe}\" \"%1\"");
        using (var k = root.CreateSubKey(@"Classes\.bar")) k.SetValue("", "NoSuchProgId");
        using (var k = root.CreateSubKey(@"Classes\.ok")) k.SetValue("", "OkFile");
        using (var k = root.CreateSubKey(@"Classes\.friendly")) k.SetValue("", "Some Friendly Name");   // ei ProgId
        using (var k = root.CreateSubKey(@"Classes\.hassub\OpenWithProgids")) { }                       // aliavain
        using (var k = root.CreateSubKey(@"Classes\.hassub")) k.SetValue("", "NoSuchProgId");
        using (var k = root.CreateSubKey(@"Classes\.nul")) k.SetValue("", "NoSuchProgId\0");           // trailing null
        using (var k = root.CreateSubKey(@"Classes\NoExtFile\shell\open\command"))
            k.SetValue("", Path.Combine(Environment.SystemDirectory, "cmd") + " /c \"%1\"");           // pääte puuttuu
        using (var k = root.CreateSubKey(@"Classes\OkFile\shell\open\command")) k.SetValue("", $"\"{Cmd}\" \"%1\"");
        using (var k = root.CreateSubKey(@"Classes\CLSID\{deadbeef}\shell\open\command")) k.SetValue("", $"\"{MissingExe}\"");

        using (var k = root.CreateSubKey("MuiCache"))
        {
            k.SetValue(MissingExe + ".FriendlyAppName", "Gone");
            k.SetValue(Cmd + ".FriendlyAppName", "Command Prompt");
            k.SetValue("@{Package}", "Resource");
        }

        using (var k = root.CreateSubKey("RecentDocs"))
        {
            k.SetValue("0", new byte[] { 1 }, RegistryValueKind.Binary);
            k.SetValue("1", new byte[] { 2 }, RegistryValueKind.Binary);
            k.SetValue("MRUListEx", new byte[] { 0 }, RegistryValueKind.Binary);
            using var ext = k.CreateSubKey(".txt");
            ext.SetValue("0", new byte[] { 3 }, RegistryValueKind.Binary);
        }
    }

    [Fact]
    public async Task Scan_finds_only_definite_orphans()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var module = CreateModule();

        var result = await module.ScanAsync(new CleanerContext { Logger = new TestLogger() });
        var ids = result.Items.Select(i => i.Id).ToList();

        Assert.Empty(result.Errors);
        Assert.Contains($"registry:uninstall:{Uninstall}\\Orphan", ids);
        Assert.DoesNotContain(ids, id => id.EndsWith(@"\Alive") || id.EndsWith(@"\Msi") || id.EndsWith(@"\Sys") || id.EndsWith(@"\DirStillThere"));

        Assert.Contains($"registry:progid:{Classes}\\FooFile", ids);
        Assert.Contains($"registry:ext:{Classes}\\.bar", ids);
        Assert.Contains($"registry:ext:{Classes}\\.nul", ids);
        Assert.DoesNotContain(ids, id => id.Contains(".foo") || id.Contains(".ok") || id.Contains("OkFile") || id.Contains("CLSID"));
        Assert.DoesNotContain(ids, id => id.Contains(".friendly") || id.Contains(".hassub") || id.Contains("NoExtFile"));

        Assert.Contains($"registry:muicache:{MuiCache}|{MissingExe}.FriendlyAppName", ids);
        Assert.Single(ids, id => id.StartsWith("registry:muicache:"));

        var mru = Assert.Single(result.Items, i => i.Category == RegistryModule.MruCategory);
        Assert.Contains("3 merkintää", mru.Description);

        Assert.Equal(6, result.Count);
        Assert.All(result.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.Description)));

        var again = await module.ScanAsync(new CleanerContext { Logger = new TestLogger() });
        Assert.Equal(ids, again.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task DryRun_changes_nothing_and_creates_no_backup()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var module = CreateModule();
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = true };

        var scan = await module.ScanAsync(ctx);
        var clean = await module.CleanAsync(scan.Items, ctx);

        Assert.True(clean.WasDryRun);
        Assert.Equal(6, clean.Removed.Count);
        Assert.Empty(clean.Skipped);
        Assert.True(RegistryPaths.KeyExists(Uninstall + @"\Orphan"));
        Assert.True(RegistryPaths.KeyExists(Classes + @"\FooFile"));
        Assert.False(Directory.Exists(_backupDir));
        Assert.Null(module.LastBackupDirectory);
    }

    [Fact]
    public async Task Apply_backs_up_then_deletes_and_restore_brings_back()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var module = CreateModule();
        var log = new TestLogger();
        var ctx = new CleanerContext { Logger = log, DryRun = false };

        var scan = await module.ScanAsync(ctx);
        var clean = await module.CleanAsync(scan.Items, ctx);

        Assert.Equal(6, clean.Removed.Count);
        Assert.Empty(clean.Skipped);

        Assert.False(RegistryPaths.KeyExists(Uninstall + @"\Orphan"));
        Assert.True(RegistryPaths.KeyExists(Uninstall + @"\Alive"));
        Assert.False(RegistryPaths.KeyExists(Classes + @"\FooFile"));
        Assert.False(RegistryPaths.KeyExists(Classes + @"\.bar"));
        Assert.True(RegistryPaths.KeyExists(Classes + @"\.foo"));
        using (var mui = RegistryPaths.OpenReadOnly(MuiCache))
        {
            Assert.Null(mui!.GetValue(MissingExe + ".FriendlyAppName"));
            Assert.NotNull(mui.GetValue(Cmd + ".FriendlyAppName"));
        }
        using (var recent = RegistryPaths.OpenReadOnly(RecentDocs))
        {
            Assert.NotNull(recent);                       // avain säilyy
            Assert.Empty(recent!.GetValueNames());
            Assert.Empty(recent.GetSubKeyNames());
        }

        // Varmuuskopio: yksi .reg per viety avain + manifest
        var backupDir = module.LastBackupDirectory;
        Assert.NotNull(backupDir);
        var regFiles = Directory.GetFiles(backupDir!, "*.reg");
        Assert.Equal(6, regFiles.Length);
        Assert.True(File.Exists(Path.Combine(backupDir!, "manifest.txt")));

        var backups = RegistryBackup.List(_backupDir);
        var info = Assert.Single(backups);
        Assert.Equal(6, info.FileCount);
        Assert.Contains("6 kohdetta", info.Summary);

        // Palautus
        var restore = RegistryBackup.Restore(info.Timestamp, log, _backupDir);
        Assert.Equal(6, restore.Imported);
        Assert.Equal(0, restore.Failed);

        Assert.True(RegistryPaths.KeyExists(Uninstall + @"\Orphan"));
        Assert.True(RegistryPaths.KeyExists(Classes + @"\FooFile"));
        Assert.True(RegistryPaths.KeyExists(Classes + @"\.bar"));
        using (var mui = RegistryPaths.OpenReadOnly(MuiCache))
            Assert.NotNull(mui!.GetValue(MissingExe + ".FriendlyAppName"));
        using (var recent = RegistryPaths.OpenReadOnly(RecentDocs))
        {
            Assert.Equal(3, recent!.GetValueNames().Length);
            Assert.Single(recent.GetSubKeyNames());
        }
    }

    [Fact]
    public async Task Guard_blocks_items_outside_roots_even_with_valid_ids()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var module = CreateModule();
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = false };

        var forged = new[]
        {
            new CleanItem($"registry:uninstall:{Classes}\\.foo", "registry", "c", "p", 0, "", RiskLevel.Low),          // väärän tyypin juuri
            new CleanItem($"registry:progid:{Classes}\\CLSID\\{{deadbeef}}", "registry", "c", "p", 0, "", RiskLevel.Low), // suojattu
            new CleanItem($"registry:progid:{Classes}\\OkFile\\shell", "registry", "c", "p", 0, "", RiskLevel.Low),      // ei suora lapsi
            new CleanItem($"registry:uninstall:HKCU\\Software\\Microsoft", "registry", "c", "p", 0, "", RiskLevel.Low),   // ulkopuolella
            new CleanItem($"registry:mru:{Root}", "registry", "c", "p", 0, "", RiskLevel.Low),                            // MRU vain täsmälleen juuri
            new CleanItem("x", "files", "c", "p", 0, "", RiskLevel.Low),                                                  // vieras moduuli
            new CleanItem("registry:bogus", "registry", "c", "p", 0, "", RiskLevel.Low),                                  // virheellinen
        };

        var clean = await module.CleanAsync(forged, ctx);

        Assert.Empty(clean.Removed);
        Assert.Equal(7, clean.Skipped.Count);
        Assert.True(RegistryPaths.KeyExists(Classes + @"\.foo"));
        Assert.True(RegistryPaths.KeyExists(Classes + @"\CLSID\{deadbeef}"));
        Assert.True(RegistryPaths.KeyExists(Classes + @"\OkFile\shell"));
        Assert.True(RegistryPaths.KeyExists(Uninstall + @"\Orphan"));
        Assert.False(Directory.Exists(_backupDir));
    }

    [Fact]
    public void Item_id_round_trips()
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = RegistryModule.MakeItemId(RegistryModule.ItemKind.MuiCache, @"HKCU\A\B", @"C:\x.exe.FriendlyAppName");
        Assert.True(RegistryModule.TryParseItemId(id, out var kind, out var key, out var value));
        Assert.Equal(RegistryModule.ItemKind.MuiCache, kind);
        Assert.Equal(@"HKCU\A\B", key);
        Assert.Equal(@"C:\x.exe.FriendlyAppName", value);

        var id2 = RegistryModule.MakeItemId(RegistryModule.ItemKind.Uninstall, @"HKLM\SOFTWARE\X\App:1");
        Assert.True(RegistryModule.TryParseItemId(id2, out kind, out key, out value));
        Assert.Equal(RegistryModule.ItemKind.Uninstall, kind);
        Assert.Equal(@"HKLM\SOFTWARE\X\App:1", key);
        Assert.Null(value);

        Assert.False(RegistryModule.TryParseItemId("registry:muicache:HKCU\\A", out _, out _, out _));
        Assert.False(RegistryModule.TryParseItemId("startup:x:y", out _, out _, out _));
    }
}
