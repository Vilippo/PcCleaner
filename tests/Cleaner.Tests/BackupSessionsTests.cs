using System.Runtime.Versioning;
using Cleaner.Core.Services;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Yhdistetty listaus ja palautus: rekisteriosuus testiavaimella HKCU\Software\PcCleanerTests\&lt;guid&gt;,
/// karanteeni ja varmuuskopiot väliaikaisissa juurissa %TEMP%\pccleaner-tests\&lt;guid&gt;.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(QuarantineTests.Collection)]   // Quarantine.SameDriveOverride on staattinen
public sealed class BackupSessionsTests : IDisposable
{
    private readonly string _sub = $@"Software\PcCleanerTests\{Guid.NewGuid():N}";
    private readonly string _root;
    private readonly string _regRoot;
    private readonly string _qRoot;
    private readonly string _data;
    private readonly TestLogger _log = new();

    public BackupSessionsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));
        _regRoot = Path.Combine(_root, "registry-backups");
        _qRoot = Path.Combine(_root, "quarantine");
        _data = Path.Combine(_root, "data");
        Directory.CreateDirectory(_data);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(_sub, throwOnMissingSubKey: false); } catch { }
            try { Registry.CurrentUser.DeleteSubKey(@"Software\PcCleanerTests", throwOnMissingSubKey: false); } catch { }
        }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Lists_union_and_restores_both_sides()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var k = Registry.CurrentUser.CreateSubKey(_sub)) k.SetValue("Marker", "palautettu");

        // Sessio 1: rekisteri + karanteeni samalla aikaleimalla.
        var backup = new RegistryBackup(_regRoot, _log);
        Assert.True(backup.TryExport($@"HKCU\{_sub}", out var exportError), exportError);
        backup.WriteManifest(new[] { "testiavain" });

        var app = Path.Combine(_data, "App");
        Directory.CreateDirectory(Path.Combine(app, "sub"));
        File.WriteAllText(Path.Combine(app, "sub", "f.txt"), "data");
        var q = new Quarantine(_qRoot, backup.Timestamp, _log);
        Assert.True(q.TryMoveDirectory(app, out _, out var moveError), moveError);
        q.WriteManifest(new[] { "App" });

        // Sessio 2: vanhempi, pelkkä karanteeni.
        const string oldTs = "20000101-000000";
        var old = Path.Combine(_data, "old.txt");
        File.WriteAllText(old, "o");
        var q2 = new Quarantine(_qRoot, oldTs, _log);
        Assert.True(q2.TryMoveFile(old, out _, out _));
        q2.WriteManifest(Array.Empty<string>());

        var sessions = BackupSessions.List(_regRoot, _qRoot);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(new BackupSessions.Session(backup.Timestamp, 1, 1, "1 .reg-tiedostoa, 1 karanteenikohdetta"), sessions[0]);
        Assert.Equal(new BackupSessions.Session(oldTs, 0, 1, "1 karanteenikohdetta"), sessions[1]);

        Registry.CurrentUser.DeleteSubKeyTree(_sub);

        var result = BackupSessions.Restore(backup.Timestamp, _log, _regRoot, _qRoot);

        Assert.Equal(1, result.RegistryImported);
        Assert.Equal(0, result.RegistryFailed);
        Assert.Equal(1, result.QuarantineRestored);
        Assert.Equal(0, result.QuarantineSkipped);
        using (var k = Registry.CurrentUser.OpenSubKey(_sub))
        {
            Assert.NotNull(k);
            Assert.Equal("palautettu", k.GetValue("Marker"));
        }
        Assert.Equal("data", File.ReadAllText(Path.Combine(app, "sub", "f.txt")));

        // Pelkkä karanteeni -sessio: puuttuva rekisteripuoli ei ole virhe.
        var r2 = BackupSessions.Restore(oldTs, _log, _regRoot, _qRoot);
        Assert.Equal(0, r2.RegistryImported);
        Assert.Equal(0, r2.RegistryFailed);
        Assert.Equal(1, r2.QuarantineRestored);
        Assert.Empty(r2.Messages);
        Assert.True(File.Exists(old));
    }

    [Fact]
    public void Restore_of_unknown_timestamp_reports_missing()
    {
        if (!OperatingSystem.IsWindows()) return;

        var result = BackupSessions.Restore("19990101-000000", _log, _regRoot, _qRoot);

        Assert.Equal(new[] { "Varmuuskopiota ei ole: 19990101-000000" }, result.Messages);
        Assert.Equal(0, result.RegistryImported + result.RegistryFailed + result.QuarantineRestored + result.QuarantineSkipped);
        Assert.Empty(BackupSessions.List(_regRoot, _qRoot));
    }
}
