using Cleaner.Ai;
using Xunit;

namespace Cleaner.Tests;

/// <summary>Testaa koneen tietojen keruuta ja koneen kunnon yhteenvetoa.</summary>
public sealed class HealthDigestTests
{
    private static SystemFacts Facts(long? available = 4L << 30) => new(
        "Microsoft Windows NT 10.0.26300.0 (64-bit)", 8, 16L << 30, available,
        TimeSpan.FromHours(50.5), new[] { new DriveFacts(@"C:\", "Windows", 500L << 30, 50L << 30) }, false);

    [Fact]
    public void Collect_does_not_throw_and_finds_a_drive()
    {
        var facts = SystemFactsCollector.Collect();
        Assert.False(string.IsNullOrEmpty(facts.OsVersion));
        Assert.True(facts.ProcessorCount >= 1);
        if (OperatingSystem.IsWindows())
        {
            Assert.NotEmpty(facts.Drives);
            Assert.NotNull(facts.AvailableMemoryBytes);
        }
    }

    [Fact]
    public void Build_contains_system_drives_and_modules()
    {
        var modules = new[]
        {
            new ModuleScanSummary("files", "File cleanup", true, 3, 3L << 20, true,
                new[] { ("Temp", 2, 2L << 20), ("Logs", 1, 1L << 20) }),
            new ModuleScanSummary("registry", "Registry", false, 0, 0, false, Array.Empty<(string, int, long)>()),
        };
        var text = HealthDigest.Build(Facts(), modules);

        Assert.Contains("# System", text);
        Assert.Contains("CPU cores: 8", text);
        Assert.Contains("Memory total: 16.0 GB, available: 4.0 GB", text);
        Assert.Contains("Uptime: 2 d 2 h 30 min", text);
        Assert.Contains("Running elevated: no", text);
        Assert.Contains("# Drives", text);
        Assert.Contains(@"- C:\ ""Windows"": size 500.0 GB, free 50.0 GB (10.0% free)", text);
        Assert.Contains("- File cleanup (files): 3 items, 3.0 MB", text);
        Assert.Contains("  - Temp: 2 items, 2.0 MB", text);
        Assert.Contains("- Registry (registry): not scanned", text);
    }

    [Fact]
    public void Unknown_memory_is_written_as_unknown()
    {
        var text = HealthDigest.Build(Facts(available: null), Array.Empty<ModuleScanSummary>());
        Assert.Contains("available: unknown", text);
    }

    [Fact]
    public void Only_five_largest_categories_are_listed()
    {
        var categories = Enumerable.Range(1, 8).Select(i => ($"Cat{i}", i, (long)i * 1000)).ToArray();
        var module = new ModuleScanSummary("files", "File cleanup", true, 36, 36000, true, categories);
        var text = HealthDigest.Build(Facts(), new[] { module });

        foreach (var i in new[] { 8, 7, 6, 5, 4 }) Assert.Contains($"  - Cat{i}:", text);
        foreach (var i in new[] { 3, 2, 1 }) Assert.DoesNotContain($"  - Cat{i}:", text);
    }

    [Fact]
    public void Unsummable_module_does_not_show_sizes()
    {
        var module = new ModuleScanSummary("diskusage", "Disk usage", true, 2, 999_999, false,
            new[] { ("Folders", 2, 999_999L) });
        var text = HealthDigest.Build(Facts(), new[] { module });

        Assert.Contains("- Disk usage (diskusage): 2 items, size not summed (nested items)", text);
        Assert.Contains("  - Folders: 2 items, size not summed (nested items)", text);
        Assert.DoesNotContain("976.6 KB", text);
    }
}
