using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Testaa karanteenisiirtoa ja palautusta väliaikaisessa kansiossa %TEMP%\pccleaner-tests\&lt;guid&gt;.
/// </summary>
[Collection(Collection)]
public sealed class QuarantineTests : IDisposable
{
    /// <summary>Quarantine.SameDriveOverride on staattinen: sitä käyttävät testiluokat eivät saa ajaa rinnakkain.</summary>
    public const string Collection = "Quarantine";

    private const string Ts = "20260101-120000";
    private readonly string _root;
    private readonly string _data;
    private readonly string _qRoot;
    private readonly TestLogger _log = new();

    public QuarantineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));
        _data = Path.Combine(_root, "data");
        _qRoot = Path.Combine(_root, "quarantine");
        Directory.CreateDirectory(_data);
    }

    public void Dispose()
    {
        Quarantine.SameDriveOverride = null;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string WriteFile(string relative, string content = "x")
    {
        var path = Path.Combine(_data, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Moves_and_restores_directory_tree()
    {
        WriteFile(@"App\a.txt", "a");
        WriteFile(@"App\sub\b.txt", "b");
        WriteFile(@"App\sub\deep\c.txt", "c");
        var app = Path.Combine(_data, "App");

        var q = new Quarantine(_qRoot, Ts, _log);
        Assert.False(Directory.Exists(q.Directory));   // laiska luonti
        Assert.True(q.TryMoveDirectory(app, out var moved, out var error), error);
        q.WriteManifest(new[] { "App" });

        Assert.True(q.HasEntries);
        Assert.False(Directory.Exists(app));
        Assert.Equal(Path.Combine(_qRoot, Ts, "001", "App"), moved);
        Assert.Equal("c", File.ReadAllText(Path.Combine(moved, "sub", "deep", "c.txt")));

        var result = Quarantine.Restore(Ts, _log, _qRoot);

        Assert.Equal(1, result.Restored);
        Assert.Equal(0, result.Skipped);
        Assert.Equal("a", File.ReadAllText(Path.Combine(app, "a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(app, "sub", "b.txt")));
        Assert.Equal("c", File.ReadAllText(Path.Combine(app, "sub", "deep", "c.txt")));
        Assert.False(Directory.Exists(Path.Combine(_qRoot, Ts, "001")));   // tyhjä NNN-kansio poistettu
    }

    [Fact]
    public void Moves_file_and_numbers_entries()
    {
        var f1 = WriteFile("one.log", "1");
        var f2 = WriteFile(@"x\two.log", "2");

        var q = new Quarantine(_qRoot, Ts, _log);
        Assert.True(q.TryMoveFile(f1, out var m1, out _));
        Assert.True(q.TryMoveFile(f2, out var m2, out _));

        Assert.False(File.Exists(f1));
        Assert.False(File.Exists(f2));
        Assert.Equal(Path.Combine(_qRoot, Ts, "001", "one.log"), m1);
        Assert.Equal(Path.Combine(_qRoot, Ts, "002", "two.log"), m2);
        Assert.Equal("2", File.ReadAllText(m2));
    }

    [Fact]
    public void Manifest_is_read_back_and_listed()
    {
        var f = WriteFile("file.txt");
        WriteFile(@"Dir\inner.txt");
        var d = Path.Combine(_data, "Dir");

        var q = new Quarantine(_qRoot, Ts, _log);
        Assert.True(q.TryMoveDirectory(d, out var md, out _));
        Assert.True(q.TryMoveFile(f, out var mf, out _));
        q.WriteManifest(new[] { "Kohde 1", "Kohde 2" });

        var entries = Quarantine.ReadManifest(q.Directory);
        Assert.Equal(2, entries.Count);
        Assert.Equal(new Quarantine.Entry("001", d, md, true), entries[0]);
        Assert.Equal(new Quarantine.Entry("002", f, mf, false), entries[1]);

        var text = File.ReadAllText(Path.Combine(q.Directory, "manifest.txt"));
        Assert.Contains($"pccleaner restore {Ts}", text);
        Assert.Contains("Käsitellyt kohteet:", text);

        // Toinen, vanhempi sessio ilman manifestia ei näy listassa.
        Directory.CreateDirectory(Path.Combine(_qRoot, "20200101-000000"));
        var list = Quarantine.List(_qRoot);
        Assert.Single(list);
        Assert.Equal(Ts, list[0].Timestamp);
        Assert.Equal(2, list[0].EntryCount);
    }

    [Fact]
    public void Restore_skips_when_original_exists()
    {
        var f = WriteFile("keep.txt", "old");
        var q = new Quarantine(_qRoot, Ts, _log);
        Assert.True(q.TryMoveFile(f, out var moved, out _));
        q.WriteManifest(Array.Empty<string>());

        File.WriteAllText(f, "new");   // joku loi saman polun uudelleen

        var result = Quarantine.Restore(Ts, _log, _qRoot);

        Assert.Equal(0, result.Restored);
        Assert.Equal(1, result.Skipped);
        Assert.Contains($"Ohitettu, polku on jo olemassa: {f}", result.Messages);
        Assert.Equal("new", File.ReadAllText(f));
        Assert.Equal("old", File.ReadAllText(moved));   // jäi karanteeniin
    }

    [Fact]
    public void PathGuard_rejection_moves_nothing()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(profile));

        var q = new Quarantine(_qRoot, Ts, _log);
        Assert.False(q.TryMoveDirectory(profile, out var moved, out var error));

        Assert.Equal("", moved);
        Assert.Contains("Suojattu", error);
        Assert.False(q.HasEntries);
        Assert.True(Directory.Exists(profile));
        Assert.False(Directory.Exists(q.Directory));
    }

    [Fact]
    public void Restore_recreates_missing_parent()
    {
        var f = WriteFile(@"gone\parent\file.txt", "p");
        var q = new Quarantine(_qRoot, Ts, _log);
        Assert.True(q.TryMoveFile(f, out _, out _));
        q.WriteManifest(Array.Empty<string>());

        Directory.Delete(Path.Combine(_data, "gone"), recursive: true);

        var result = Quarantine.Restore(Ts, _log, _qRoot);

        Assert.Equal(1, result.Restored);
        Assert.Equal("p", File.ReadAllText(f));
    }

    [Fact]
    public void Cross_drive_move_copies_deletes_and_restores()
    {
        Quarantine.SameDriveOverride = (_, _) => false;
        WriteFile(@"App\a.txt", "a");
        WriteFile(@"App\sub\b.txt", "b");
        var app = Path.Combine(_data, "App");

        var q = new Quarantine(_qRoot, Ts, _log);
        Assert.True(q.TryMoveDirectory(app, out var moved, out var error), error);
        q.WriteManifest(Array.Empty<string>());

        Assert.False(Directory.Exists(app));
        Assert.Equal("b", File.ReadAllText(Path.Combine(moved, "sub", "b.txt")));

        var result = Quarantine.Restore(Ts, _log, _qRoot);

        Assert.Equal(1, result.Restored);
        Assert.Equal("a", File.ReadAllText(Path.Combine(app, "a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(app, "sub", "b.txt")));
        Assert.False(Directory.Exists(Path.Combine(_qRoot, Ts, "001")));
    }

    [Fact]
    public void Cross_drive_delete_failure_keeps_copy_and_consumes_slot()
    {
        Quarantine.SameDriveOverride = (_, _) => false;
        WriteFile(@"Locked\a.txt", "a");
        var locked = WriteFile(@"Locked\held.txt", "h");
        var dir = Path.Combine(_data, "Locked");
        var next = WriteFile("next.txt", "n");

        var q = new Quarantine(_qRoot, Ts, _log);
        string moved, error;
        // Luku sallittu (kopio onnistuu), poisto ei (FileShare.Delete puuttuu).
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(q.TryMoveDirectory(dir, out moved, out error));

        var slot1 = Path.Combine(_qRoot, Ts, "001", "Locked");
        Assert.Equal(slot1, moved);
        Assert.StartsWith($"Alkuperäisen poisto epäonnistui, kopio on karanteenissa: {slot1}. ", error);
        Assert.Equal("a", File.ReadAllText(Path.Combine(slot1, "a.txt")));
        Assert.Equal("h", File.ReadAllText(Path.Combine(slot1, "held.txt")));
        Assert.True(File.Exists(locked));   // lukittu alkuperäinen jäi paikalleen
        Assert.True(q.HasEntries);

        Assert.True(q.TryMoveFile(next, out var movedNext, out _));
        Assert.Equal(Path.Combine(_qRoot, Ts, "002", "next.txt"), movedNext);

        q.WriteManifest(Array.Empty<string>());
        var entries = Quarantine.ReadManifest(q.Directory);
        Assert.Equal(new Quarantine.Entry("001", dir, slot1, true), entries[0]);
        Assert.Equal("002", entries[1].Index);
    }

    [Fact]
    public void Restore_skips_quarantine_path_outside_session()
    {
        var outside = WriteFile("outside.txt", "o");
        var original = Path.Combine(_data, "restored.txt");
        var session = Path.Combine(_qRoot, Ts);
        Directory.CreateDirectory(session);
        File.WriteAllLines(Path.Combine(session, "manifest.txt"), new[] { $"001\tF\t{original}\t{outside}" });

        var result = Quarantine.Restore(Ts, _log, _qRoot);

        Assert.Equal(0, result.Restored);
        Assert.Equal(1, result.Skipped);
        Assert.Contains($"Ohitettu, karanteenipolku ei ole session kansiossa: {outside}", result.Messages);
        Assert.True(File.Exists(outside));
        Assert.False(File.Exists(original));
    }
}
