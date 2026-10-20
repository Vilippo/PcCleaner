using System.Runtime.Versioning;
using Cleaner.Modules.Uninstall;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Tests;

/// <summary>Lukee testiavaimen HKCU\Software\PcCleanerTests\&lt;guid&gt;\Uninstall (CON-8).</summary>
[SupportedOSPlatform("windows")]
public sealed class InstalledProgramReaderTests : IDisposable
{
    private const string MsiKey = "{11111111-2222-3333-4444-555555555555}";
    private readonly string _sub = $@"Software\PcCleanerTests\{Guid.NewGuid():N}";

    private string Uninstall => @"HKCU\" + _sub + @"\Uninstall";

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(_sub, throwOnMissingSubKey: false); } catch { }
            try { Registry.CurrentUser.DeleteSubKey(@"Software\PcCleanerTests", throwOnMissingSubKey: false); } catch { }
        }
    }

    private void CreateEntry(string keyName, params (string Name, object Value)[] values)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_sub + @"\Uninstall\" + keyName);
        foreach (var (name, value) in values)
            key.SetValue(name, value, value is int ? RegistryValueKind.DWord : RegistryValueKind.String);
    }

    private void Seed()
    {
        CreateEntry("Normaali",
            ("DisplayName", "Normaali Ohjelma"),
            ("DisplayVersion", "1.2.3"),
            ("Publisher", "Testi Oy"),
            ("InstallLocation", @"""C:\Foo\"""),
            ("UninstallString", @"""C:\Foo\unins000.exe"" /S"),
            ("QuietUninstallString", @"""C:\Foo\unins000.exe"" /VERYSILENT"),
            ("DisplayIcon", @"C:\Foo\app.exe,0"),
            ("EstimatedSize", 2048),
            ("InstallDate", "20260315"));
        CreateEntry(MsiKey,
            ("DisplayName", "Msi Ohjelma"),
            ("WindowsInstaller", 1),
            ("UninstallString", "MsiExec.exe /X" + MsiKey),
            ("InstallDate", "2026-3"));   // ei jäsenny → null
        CreateEntry("Sys", ("DisplayName", "Sys"), ("SystemComponent", 1));
        CreateEntry("NoName", ("DisplayVersion", "1.0"));
        CreateEntry("Update", ("DisplayName", "Update KB123"), ("ParentKeyName", "Normaali"));
    }

    [Fact]
    public void Lists_only_visible_programs_sorted_by_name()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();

        var list = InstalledProgramReader.List(new[] { Uninstall });

        Assert.Equal(new[] { "Msi Ohjelma", "Normaali Ohjelma" }, list.Select(p => p.Name));

        var msi = list[0];
        Assert.Equal(MsiKey, msi.KeyName);
        Assert.Equal(Uninstall + @"\" + MsiKey, msi.KeyPath);
        Assert.True(msi.IsMsi);
        Assert.Equal(0, msi.EstimatedSizeBytes);
        Assert.Null(msi.InstallLocation);
        Assert.Null(msi.InstallDate);

        var normal = list[1];
        Assert.Equal("Normaali", normal.KeyName);
        Assert.Equal(Uninstall + @"\Normaali", normal.KeyPath);
        Assert.Equal("1.2.3", normal.Version);
        Assert.Equal("Testi Oy", normal.Publisher);
        Assert.Equal(@"C:\Foo", normal.InstallLocation);
        Assert.Equal(@"""C:\Foo\unins000.exe"" /S", normal.UninstallString);
        Assert.Equal(@"""C:\Foo\unins000.exe"" /VERYSILENT", normal.QuietUninstallString);
        Assert.Equal(@"C:\Foo\app.exe,0", normal.DisplayIcon);
        Assert.Equal(2_097_152, normal.EstimatedSizeBytes);
        Assert.False(normal.IsMsi);
        Assert.Equal(new DateOnly(2026, 3, 15), normal.InstallDate);
    }

    [Theory]
    [InlineData("20260315", 2026, 3, 15)]
    [InlineData("2026-03-15", 2026, 3, 15)]
    [InlineData(" 20240101 ", 2024, 1, 1)]
    public void ParseInstallDate_accepts_common_formats(string value, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), InstalledProgramReader.ParseInstallDate(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("20261345")]
    [InlineData("15.03.2026")]
    public void ParseInstallDate_rejects_unknown_values(string? value)
    {
        Assert.Null(InstalledProgramReader.ParseInstallDate(value));
    }

    [Fact]
    public void Sort_by_install_date_puts_newest_first_and_undated_last()
    {
        static InstalledProgram P(string name, DateOnly? date) =>
            new(@"HKCU\X\" + name, name, name, null, null, null, null, null, null, 0, false, date);

        var programs = new[]
        {
            P("Beta", new DateOnly(2026, 1, 10)),
            P("Alpha", null),
            P("Gamma", new DateOnly(2026, 5, 1)),
            P("Delta", new DateOnly(2026, 1, 10)),
            P("Zeta", null),
        };

        var byDate = InstalledProgramReader.Sort(programs, ProgramSortOrder.InstallDateDescending);
        Assert.Equal(new[] { "Gamma", "Beta", "Delta", "Alpha", "Zeta" }, byDate.Select(p => p.Name));

        var byName = InstalledProgramReader.Sort(programs, ProgramSortOrder.Name);
        Assert.Equal(new[] { "Alpha", "Beta", "Delta", "Gamma", "Zeta" }, byName.Select(p => p.Name));
    }

    [Fact]
    public void Missing_root_is_skipped()
    {
        if (!OperatingSystem.IsWindows()) return;
        Seed();

        var list = InstalledProgramReader.List(new[] { Uninstall + @"\NoSuchRoot", Uninstall });

        Assert.Equal(2, list.Count);
    }
}
