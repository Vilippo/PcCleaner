using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Modules.Files;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Testaa moduulia oikealla, väliaikaisella kansiorakenteella.
/// Jokainen testi saa oman kansion ja siivoaa sen lopuksi.
/// </summary>
public sealed class FileCleanerModuleTests : IDisposable
{
    private readonly string _root;

    public FileCleanerModuleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private FileCleanerModule CreateModule(int minAgeHours, params string[] patterns) =>
        new("test", "Test", "", new[]
        {
            new CleanRule
            {
                Id = "r",
                Name = "Testisääntö",
                Paths = new List<string> { _root },
                Patterns = patterns.ToList(),
                MinAgeHours = minAgeHours
            }
        });

    private string WriteFile(string relative, int bytes, TimeSpan? age = null)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        if (age is not null)
        {
            var t = DateTime.UtcNow - age.Value;
            File.SetLastWriteTimeUtc(path, t);
            File.SetCreationTimeUtc(path, t);
        }
        return path;
    }

    [Fact]
    public async Task Scan_finds_old_files_and_skips_fresh_ones()
    {
        WriteFile("old.tmp", 100, TimeSpan.FromHours(48));
        WriteFile("fresh.tmp", 100, TimeSpan.FromMinutes(5));

        var module = CreateModule(minAgeHours: 24);
        var result = await module.ScanAsync(new CleanerContext { Logger = new TestLogger() });

        var item = Assert.Single(result.Items);
        Assert.EndsWith("old.tmp", item.Path);
        Assert.Equal(100, result.TotalBytes);
    }

    [Fact]
    public async Task Scan_respects_patterns()
    {
        WriteFile("a.log", 10, TimeSpan.FromHours(48));
        WriteFile("b.txt", 10, TimeSpan.FromHours(48));

        var module = CreateModule(0, "*.log");
        var result = await module.ScanAsync(new CleanerContext { Logger = new TestLogger() });

        Assert.Single(result.Items);
        Assert.EndsWith("a.log", result.Items[0].Path);
    }

    [Fact]
    public async Task DryRun_deletes_nothing()
    {
        var path = WriteFile("sub/old.tmp", 10, TimeSpan.FromHours(48));
        var module = CreateModule(0);
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = true };

        var scan = await module.ScanAsync(ctx);
        var clean = await module.CleanAsync(scan.Items, ctx);

        Assert.True(clean.WasDryRun);
        Assert.Single(clean.Removed);
        Assert.True(File.Exists(path), "Kuivan ajon ei pidä poistaa mitään.");
    }

    [Fact]
    public async Task Apply_deletes_files_and_empty_subdirs_but_not_root()
    {
        var path = WriteFile("sub/deep/old.tmp", 10, TimeSpan.FromHours(48));
        var module = CreateModule(0);
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = false };

        var scan = await module.ScanAsync(ctx);
        var clean = await module.CleanAsync(scan.Items, ctx);

        Assert.Single(clean.Removed);
        Assert.Empty(clean.Skipped);
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.Combine(_root, "sub")), "Tyhjät alikansiot pitää poistaa.");
        Assert.True(Directory.Exists(_root), "Säännön juurikansiota ei saa poistaa.");
    }

    [Fact]
    public async Task Clean_rejects_items_from_other_module()
    {
        var module = CreateModule(0);
        var ctx = new CleanerContext { Logger = new TestLogger(), DryRun = false };
        var foreign = new CleanItem("x", "other", "c", Path.Combine(_root, "x"), 0, "", RiskLevel.Low);

        var clean = await module.CleanAsync(new[] { foreign }, ctx);

        Assert.Empty(clean.Removed);
        Assert.Single(clean.Skipped);
    }
}
