using Cleaner.Core.Models;
using Cleaner.Modules.DiskUsage;
using Xunit;

namespace Cleaner.Tests;

/// <summary>Testaa moduulia oikealla väliaikaisella kansiorakenteella.</summary>
public sealed class DiskUsageModuleTests : IDisposable
{
    private readonly string _root;

    public DiskUsageModuleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string WriteFile(string relative, int bytes)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    /// <summary>
    /// root/
    ///   top.bin        10
    ///   a/one.bin     100
    ///   a/two.bin     200
    ///   a/sub/three.bin 300   → a = 600
    ///   b/big.bin    1000     → b = 1000
    /// </summary>
    private void Seed()
    {
        WriteFile("top.bin", 10);
        WriteFile("a/one.bin", 100);
        WriteFile("a/two.bin", 200);
        WriteFile("a/sub/three.bin", 300);
        WriteFile("b/big.bin", 1000);
    }

    private DiskUsageModule CreateModule(int top = 3, int parallelism = 2) =>
        new(new DiskUsageOptions { Roots = new[] { _root }, TopFolders = top, TopFiles = top, MaxDegreeOfParallelism = parallelism });

    [Fact]
    public async Task Scan_builds_tree_with_correct_sizes()
    {
        Seed();
        var module = CreateModule();

        await module.ScanAsync(new CleanerContext { Logger = new TestLogger() });

        var root = Assert.Single(module.LastTrees);
        Assert.Equal(1610, root.TotalBytes);
        Assert.Equal(10, root.OwnBytes);
        Assert.Equal(5, root.FileCount);
        Assert.False(root.HadErrors);

        var a = root.Children.Single(c => c.Path.EndsWith("a"));
        var b = root.Children.Single(c => c.Path.EndsWith("b"));
        Assert.Equal(600, a.TotalBytes);
        Assert.Equal(300, a.OwnBytes);
        Assert.Equal(3, a.FileCount);
        Assert.Equal(1000, b.TotalBytes);
    }

    [Fact]
    public async Task Scan_reports_largest_folders_and_files_in_order_excluding_root()
    {
        Seed();
        var module = CreateModule(top: 2);

        var result = await module.ScanAsync(new CleanerContext { Logger = new TestLogger() });

        var folders = result.Items.Where(i => i.Category == DiskUsageModule.FolderCategory).ToList();
        var files = result.Items.Where(i => i.Category == DiskUsageModule.FileCategory).ToList();

        Assert.Equal(2, folders.Count);
        Assert.EndsWith("b", folders[0].Path);
        Assert.Equal(1000, folders[0].SizeBytes);
        Assert.EndsWith("a", folders[1].Path);
        Assert.Equal(600, folders[1].SizeBytes);
        Assert.DoesNotContain(folders, f => f.Path == _root);
        Assert.Contains("62 %", folders[0].Description);

        Assert.Equal(2, files.Count);
        Assert.EndsWith("big.bin", files[0].Path);
        Assert.EndsWith("three.bin", files[1].Path);
        Assert.All(result.Items, i => Assert.Equal(RiskLevel.High, i.Risk));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Scan_is_idempotent_and_parallelism_does_not_change_result()
    {
        Seed();
        var ctx = new CleanerContext { Logger = new TestLogger() };

        var serial = await CreateModule(parallelism: 1).ScanAsync(ctx);
        var parallel = await CreateModule(parallelism: 8).ScanAsync(ctx);
        var again = await CreateModule(parallelism: 8).ScanAsync(ctx);

        Assert.Equal(serial.Items.Select(i => i.Id), parallel.Items.Select(i => i.Id));
        Assert.Equal(parallel.Items.Select(i => i.Id), again.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Missing_root_is_reported_as_error_not_exception()
    {
        var module = new DiskUsageModule(new DiskUsageOptions { Roots = new[] { Path.Combine(_root, "nope") } });

        var result = await module.ScanAsync(new CleanerContext { Logger = new TestLogger() });

        Assert.Empty(result.Items);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task DryRun_deletes_nothing()
    {
        Seed();
        var module = CreateModule();
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = true };

        var scan = await module.ScanAsync(ctx);
        var clean = await module.CleanAsync(scan.Items, ctx);

        Assert.True(clean.WasDryRun);
        Assert.Equal(3, clean.Removed.Count);           // tiedostot "poistettaisiin"
        Assert.Equal(3, clean.Skipped.Count);           // kansiot ohitetaan aina
        Assert.True(File.Exists(Path.Combine(_root, "b", "big.bin")));
        Assert.True(Directory.Exists(Path.Combine(_root, "b")));
    }

    [Fact]
    public async Task Apply_deletes_files_but_never_folders()
    {
        Seed();
        var module = CreateModule();
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = false };

        var scan = await module.ScanAsync(ctx);
        var clean = await module.CleanAsync(scan.Items, ctx);

        Assert.Equal(3, clean.Removed.Count);
        Assert.False(File.Exists(Path.Combine(_root, "b", "big.bin")));
        Assert.False(File.Exists(Path.Combine(_root, "a", "sub", "three.bin")));
        Assert.True(File.Exists(Path.Combine(_root, "top.bin")));          // ei top-3:ssa
        Assert.True(Directory.Exists(Path.Combine(_root, "b")));
        Assert.All(clean.Skipped.Where(s => s.Item.Category == DiskUsageModule.FolderCategory),
            s => Assert.Contains("Kansioita ei poisteta", s.Reason));
    }

    [Fact]
    public async Task Foreign_items_are_skipped()
    {
        var module = CreateModule();
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = false };
        var foreign = new CleanItem("x", "files", "c", Path.Combine(_root, "x"), 0, "", RiskLevel.Low);

        var clean = await module.CleanAsync(new[] { foreign }, ctx);

        Assert.Empty(clean.Removed);
        Assert.Single(clean.Skipped);
    }

    [Fact]
    public async Task Cancellation_stops_scan()
    {
        Seed();
        var module = CreateModule();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => module.ScanAsync(new CleanerContext { Logger = new TestLogger() }, cts.Token));
    }
}
