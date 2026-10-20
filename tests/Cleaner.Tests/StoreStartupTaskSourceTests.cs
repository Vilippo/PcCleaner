using System.Runtime.Versioning;
using Cleaner.Core.Models;
using Cleaner.Modules.Startup;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Store/MSIX StartupTask -lähde omassa HKCU\Software\PcCleanerTests\&lt;guid&gt;-avaimessa: SystemAppData- ja
/// Repository\Packages-rakenne jäljitellään sinne. Ajetaan vain Windowsissa.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StoreStartupTaskSourceTests : IDisposable
{
    private readonly string _testKey = $@"Software\PcCleanerTests\{Guid.NewGuid():N}";

    private string SystemAppData => _testKey + @"\SystemAppData";
    private string Packages => _testKey + @"\Packages";

    private const string FooFamily = "Acme.Foo_abc123xyz";
    private const string FooFull = "Acme.Foo_1.2.3.0_x64__abc123xyz";
    private const string BarFamily = "Bar.App_q1w2e3r4";

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { Registry.CurrentUser.DeleteSubKeyTree(_testKey, throwOnMissingSubKey: false); } catch { }
        try { Registry.CurrentUser.DeleteSubKey(@"Software\PcCleanerTests", throwOnMissingSubKey: false); } catch { }
    }

    private StoreStartupTaskSource Source() => new(SystemAppData, Packages);

    private void Seed()
    {
        using (var k = Registry.CurrentUser.CreateSubKey($@"{SystemAppData}\{FooFamily}\FooStart"))
            k.SetValue("State", 2, RegistryValueKind.DWord);                        // Enabled
        using (var k = Registry.CurrentUser.CreateSubKey($@"{SystemAppData}\{FooFamily}\FooOther"))
            k.SetValue("State", 1, RegistryValueKind.DWord);                        // DisabledByUser
        using (var k = Registry.CurrentUser.CreateSubKey($@"{SystemAppData}\{FooFamily}\SplashScreen"))
            k.SetValue("Color", "red");                                              // ei State-arvoa
        using (var k = Registry.CurrentUser.CreateSubKey($@"{SystemAppData}\{BarFamily}\Run"))
            k.SetValue("State", 4, RegistryValueKind.DWord);                        // EnabledByPolicy
        using (var k = Registry.CurrentUser.CreateSubKey($@"{SystemAppData}\{BarFamily}\Text"))
            k.SetValue("State", "2");                                                // väärä tyyppi, ohitetaan
        using (var k = Registry.CurrentUser.CreateSubKey($@"{Packages}\{FooFull}"))
            k.SetValue("DisplayName", "Foo App");
    }

    private int? State(string family, string task)
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"{SystemAppData}\{family}\{task}");
        return k?.GetValue("State") as int?;
    }

    [Fact]
    public void Name_round_trips_and_rejects_bad_forms()
    {
        var name = StoreStartupTaskSource.MakeName("Pkg_abc", "Task.Id");
        Assert.Equal("Pkg_abc|Task.Id", name);
        Assert.True(StoreStartupTaskSource.TryParseName(name, out var family, out var task));
        Assert.Equal("Pkg_abc", family);
        Assert.Equal("Task.Id", task);

        Assert.False(StoreStartupTaskSource.TryParseName("Pkg_abc", out _, out _));
        Assert.False(StoreStartupTaskSource.TryParseName("|Task", out _, out _));
        Assert.False(StoreStartupTaskSource.TryParseName("Pkg|", out _, out _));
        Assert.False(StoreStartupTaskSource.TryParseName("Pkg|a|b", out _, out _));
        Assert.False(StoreStartupTaskSource.TryParseName(@"..\Pkg|Task", out _, out _));
    }

    [Fact]
    public void ListEnabled_returns_enabled_tasks_with_display_names()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();

        var tasks = Source().ListEnabled();

        Assert.Equal(2, tasks.Count);
        var foo = Assert.Single(tasks, t => t.PackageFamily == FooFamily);
        Assert.Equal("FooStart", foo.TaskId);
        Assert.Equal("Foo App", foo.DisplayName);
        Assert.Equal(StoreStartupTaskSource.TaskState.Enabled, foo.State);
        var bar = Assert.Single(tasks, t => t.PackageFamily == BarFamily);
        Assert.Equal("Run", bar.TaskId);
        Assert.Equal("Bar.App", bar.DisplayName); // ei Repository-merkintää: paketin nimiosa
        Assert.Equal(StoreStartupTaskSource.TaskState.EnabledByPolicy, bar.State);
    }

    [Fact]
    public void Missing_root_lists_nothing()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Empty(Source().ListEnabled());
        Assert.Null(Source().GetState(FooFamily, "FooStart"));
    }

    [Fact]
    public void ResolveIndirect_passes_plain_text_and_returns_null_for_unknown_resource()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal("Plain", StoreStartupTaskSource.ResolveIndirect("Plain", FooFull, "Acme.Foo"));
        Assert.Null(StoreStartupTaskSource.ResolveIndirect("ms-resource:AppName", FooFull, "Acme.Foo"));
        Assert.Null(StoreStartupTaskSource.ResolveIndirect("@{" + FooFull + "?ms-resource://Acme.Foo/Resources/AppName}", FooFull, "Acme.Foo"));
    }

    [Fact]
    public void Default_source_resolves_real_package_names_when_present()
    {
        if (!OperatingSystem.IsWindows()) return;
        // Koneesta riippuva: jos StartupTask-kohteita ei ole, ei väitetä mitään. Jos on, nimi ei saa olla tyhjä.
        foreach (var task in StoreStartupTaskSource.Default.ListEnabled())
            Assert.False(string.IsNullOrWhiteSpace(task.DisplayName));
    }

    [Fact]
    public async Task Module_scans_disables_and_skips_policy_tasks()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();
        var source = Source();
        var module = new StartupModule(Array.Empty<StartupSource>(), includeScheduledTasks: false, source);
        var log = new TestLogger();

        var scan = await module.ScanAsync(new CleanerContext { Logger = log });
        Assert.Equal(2, scan.Count);
        var fooId = StartupModule.MakeItemId(StoreStartupTaskSource.Key, StoreStartupTaskSource.MakeName(FooFamily, "FooStart"));
        var foo = Assert.Single(scan.Items, i => i.Id == fooId);
        Assert.Equal(StoreStartupTaskSource.Category, foo.Category);
        Assert.Contains("Foo App", foo.Description);
        Assert.EndsWith($@"\{FooFamily}\FooStart", foo.Path);
        Assert.Empty(scan.Errors);

        var dry = await module.CleanAsync(scan.Items, new CleanerContext { Logger = log, DryRun = true });
        Assert.Single(dry.Removed);
        Assert.Single(dry.Skipped, s => s.Reason == "Käytäntö pakottaa käyttöön");
        Assert.Equal(2, State(FooFamily, "FooStart"));

        var real = await module.CleanAsync(scan.Items, new CleanerContext { Logger = log, DryRun = false });
        Assert.Single(real.Removed);
        Assert.Equal(1, State(FooFamily, "FooStart"));    // DisabledByUser
        Assert.Equal(4, State(BarFamily, "Run"));         // ei kosketa

        var again = await module.ScanAsync(new CleanerContext { Logger = log });
        Assert.DoesNotContain(again.Items, i => i.Id == fooId);

        var repeat = await module.CleanAsync(new[] { foo }, new CleanerContext { Logger = log, DryRun = false });
        Assert.Single(repeat.Skipped, s => s.Reason == "On jo pois käytöstä");

        source.Enable(FooFamily, "FooStart");
        Assert.Equal(2, State(FooFamily, "FooStart"));
    }

    [Fact]
    public async Task Module_without_store_source_ignores_store_items()
    {
        if (!OperatingSystem.IsWindows()) return;
        var module = new StartupModule(Array.Empty<StartupSource>(), includeScheduledTasks: false);
        var item = new CleanItem(StartupModule.MakeItemId(StoreStartupTaskSource.Key, "X_y|T"), "startup",
            StoreStartupTaskSource.Category, "HKCU\\x", 0, "", RiskLevel.Medium);

        var scan = await module.ScanAsync(new CleanerContext { Logger = new TestLogger() });
        var clean = await module.CleanAsync(new[] { item }, new CleanerContext { Logger = new TestLogger() });

        Assert.Empty(scan.Items);
        Assert.Single(clean.Skipped, s => s.Reason == "StartupTask-lähde ei ole käytössä");
    }
}
