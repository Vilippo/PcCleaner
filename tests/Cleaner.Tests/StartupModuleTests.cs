using System.Runtime.Versioning;
using Cleaner.Core.Models;
using Cleaner.Modules.Startup;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Tests;

[SupportedOSPlatform("windows")]
public sealed class StartupApprovedTests
{
    [Fact]
    public void Missing_value_means_enabled() => Assert.False(StartupApproved.IsDisabled(null));

    [Theory]
    [InlineData(0x02, false)]
    [InlineData(0x03, true)]
    [InlineData(0x06, false)]
    [InlineData(0x07, true)]
    public void State_byte_low_bit_decides(byte state, bool disabled)
    {
        var value = new byte[12];
        value[0] = state;
        Assert.Equal(disabled, StartupApproved.IsDisabled(value));
    }

    [Fact]
    public void Disabled_value_has_state_and_filetime()
    {
        var when = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var value = StartupApproved.BuildDisabledValue(when);

        Assert.Equal(12, value.Length);
        Assert.Equal(StartupApproved.DisabledState, value[0]);
        Assert.Equal(when, DateTime.FromFileTimeUtc(BitConverter.ToInt64(value, 4)));
        Assert.True(StartupApproved.IsDisabled(value));
    }

    [Fact]
    public void Item_id_round_trips_with_colons_in_name()
    {
        var id = StartupModule.MakeItemId("hkcu-run", "C:\\Weird:Name");
        Assert.True(StartupModule.TryParseItemId(id, out var source, out var name));
        Assert.Equal("hkcu-run", source);
        Assert.Equal("C:\\Weird:Name", name);
        Assert.False(StartupModule.TryParseItemId("files:x", out _, out _));
        Assert.False(StartupModule.TryParseItemId("startup::x", out _, out _));
    }
}

/// <summary>
/// Testaa moduulia oikealla rekisterillä, mutta omassa HKCU\Software\PcCleanerTests\<guid>-avaimessa
/// ja omassa väliaikaiskansiossa. Ajetaan vain Windowsissa.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupModuleTests : IDisposable
{
    private readonly string _testKey = $@"Software\PcCleanerTests\{Guid.NewGuid():N}";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));

    private string RunKey => _testKey + @"\Run";
    private string ApprovedRunKey => _testKey + @"\StartupApproved\Run";
    private string ApprovedFolderKey => _testKey + @"\StartupApproved\StartupFolder";

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(_testKey, throwOnMissingSubKey: false); } catch { }
            // Yläavain poistetaan vain, jos se jäi tyhjäksi (DeleteSubKey ei poista avainta, jolla on aliavaimia).
            try { Registry.CurrentUser.DeleteSubKey(@"Software\PcCleanerTests", throwOnMissingSubKey: false); } catch { }
        }
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private StartupModule CreateModule() => new(new[]
    {
        new StartupSource("test-run", "Testi Run", StartupSourceKind.RegistryRun,
            RegistryHive.CurrentUser, RunKey, ApprovedRunKey, RequiresAdmin: false),
        new StartupSource("test-folder", "Testikansio", StartupSourceKind.Folder,
            RegistryHive.CurrentUser, _folder, ApprovedFolderKey, RequiresAdmin: false),
        new StartupSource("test-admin", "Testi admin", StartupSourceKind.RegistryRun,
            RegistryHive.CurrentUser, RunKey, ApprovedRunKey, RequiresAdmin: true),
    }, includeScheduledTasks: false);

    private void Seed()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            run.SetValue("Alpha", @"C:\apps\alpha.exe --tray");
            run.SetValue("Beta", @"C:\apps\beta.exe");
            run.SetValue("Binary", new byte[] { 1, 2 }, RegistryValueKind.Binary); // ei komento, ohitetaan
        }
        using (var approved = Registry.CurrentUser.CreateSubKey(ApprovedRunKey))
        {
            approved.SetValue("Beta", StartupApproved.BuildDisabledValue(DateTime.UtcNow), RegistryValueKind.Binary);
        }

        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "gamma.bat"), "@echo off");
        File.WriteAllText(Path.Combine(_folder, "desktop.ini"), "");
    }

    private static byte[]? ApprovedValue(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(name) as byte[];
    }

    [Fact]
    public async Task Scan_returns_only_enabled_entries_and_is_idempotent()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var module = CreateModule();
        var ctx = new CleanerContext { Logger = new TestLogger() };

        var first = await module.ScanAsync(ctx);
        var second = await module.ScanAsync(ctx);

        // Alpha kahdesti (test-run ja test-admin jakavat avaimen), gamma.bat kerran. Beta on pois käytöstä, Binary ei komento.
        Assert.Equal(3, first.Count);
        Assert.Contains(first.Items, i => i.Id == "startup:test-run:Alpha" && i.Description.Contains("alpha.exe"));
        Assert.Contains(first.Items, i => i.Id == "startup:test-folder:gamma.bat");
        Assert.DoesNotContain(first.Items, i => i.Id.EndsWith(":Beta"));
        Assert.DoesNotContain(first.Items, i => i.Id.EndsWith("desktop.ini"));
        Assert.All(first.Items, i => Assert.Equal(0, i.SizeBytes));
        Assert.Equal(first.Items.Select(i => i.Id), second.Items.Select(i => i.Id));
        Assert.Empty(first.Errors);
    }

    [Fact]
    public async Task DryRun_changes_nothing_but_reports()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var module = CreateModule();
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = true };

        var scan = await module.ScanAsync(ctx);
        var items = scan.Items.Where(i => i.Id.StartsWith("startup:test-run:") || i.Id.StartsWith("startup:test-folder:")).ToList();
        var clean = await module.CleanAsync(items, ctx);

        Assert.True(clean.WasDryRun);
        Assert.Equal(2, clean.Removed.Count);
        Assert.Empty(clean.Skipped);
        Assert.Null(ApprovedValue(ApprovedRunKey, "Alpha"));
        Assert.Null(ApprovedValue(ApprovedFolderKey, "gamma.bat"));
        Assert.True(File.Exists(Path.Combine(_folder, "gamma.bat")));
    }

    [Fact]
    public async Task Apply_writes_disabled_marker_and_next_scan_omits_entry()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var module = CreateModule();
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = false };

        var scan = await module.ScanAsync(ctx);
        var items = scan.Items.Where(i => i.Id.StartsWith("startup:test-run:") || i.Id.StartsWith("startup:test-folder:")).ToList();
        var clean = await module.CleanAsync(items, ctx);

        Assert.Equal(2, clean.Removed.Count);
        Assert.Empty(clean.Skipped);
        Assert.True(StartupApproved.IsDisabled(ApprovedValue(ApprovedRunKey, "Alpha")));
        Assert.True(StartupApproved.IsDisabled(ApprovedValue(ApprovedFolderKey, "gamma.bat")));

        // Run-arvo ja tiedosto ovat yhä paikallaan: vain käytöstä poisto.
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        Assert.NotNull(run!.GetValue("Alpha"));
        Assert.True(File.Exists(Path.Combine(_folder, "gamma.bat")));

        var again = await module.ScanAsync(ctx);
        Assert.DoesNotContain(again.Items, i => i.Id == "startup:test-run:Alpha");
        Assert.DoesNotContain(again.Items, i => i.Id == "startup:test-folder:gamma.bat");
    }

    [Fact]
    public async Task Admin_source_is_skipped_without_elevation()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var module = CreateModule();
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = false, IsElevated = false };

        var scan = await module.ScanAsync(ctx);
        var adminItems = scan.Items.Where(i => i.Id.StartsWith("startup:test-admin:")).ToList();
        var clean = await module.CleanAsync(adminItems, ctx);

        Assert.Single(adminItems);
        Assert.Empty(clean.Removed);
        Assert.Single(clean.Skipped);
        Assert.Contains("järjestelmänvalvojan", clean.Skipped[0].Reason);
        Assert.Null(ApprovedValue(ApprovedRunKey, "Alpha"));
    }

    [Fact]
    public async Task Foreign_and_malformed_items_are_skipped()
    {
        if (!OperatingSystem.IsWindows()) return;
        var module = CreateModule();
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = false };
        var foreign = new CleanItem("x", "files", "c", "p", 0, "", RiskLevel.Low);
        var malformed = new CleanItem("startup:nope", "startup", "c", "p", 0, "", RiskLevel.Low);
        var missing = new CleanItem("startup:test-run:DoesNotExist", "startup", "c", "p", 0, "", RiskLevel.Low);

        var clean = await module.CleanAsync(new[] { foreign, malformed, missing }, ctx);

        Assert.Empty(clean.Removed);
        Assert.Equal(3, clean.Skipped.Count);
    }
}
