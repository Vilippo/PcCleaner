using Cleaner.Core.Logging;
using Xunit;

namespace Cleaner.Tests;

public sealed class FileLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));

    public FileLoggerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void Touch(string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }

    [Fact]
    public void Prune_keeps_newest_count_and_drops_old_ones()
    {
        var now = DateTime.UtcNow;
        for (int i = 0; i < 10; i++)
            Touch($"cleaner-new-{i:00}.log", now.AddMinutes(-i));          // tuoreet
        Touch("cleaner-ancient.log", now.AddDays(-40));                    // liian vanha
        Touch("other.log", now.AddDays(-40));                              // ei meidän
        Touch("cleaner-noext.txt", now.AddDays(-40));                      // väärä pääte

        var removed = FileLogger.PruneOldLogs(_dir, keepCount: 5, keepDays: 30);

        var left = Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(6, removed);                                          // 5 ylimääräistä tuoretta + ancient
        Assert.Equal(5, left.Count(n => n!.StartsWith("cleaner-new-")));
        Assert.Contains("cleaner-new-00.log", left);
        Assert.Contains("cleaner-new-04.log", left);
        Assert.DoesNotContain("cleaner-new-05.log", left);
        Assert.DoesNotContain("cleaner-ancient.log", left);
        Assert.Contains("other.log", left);
        Assert.Contains("cleaner-noext.txt", left);
    }

    [Fact]
    public void Constructor_prunes_and_creates_new_log()
    {
        var now = DateTime.UtcNow;
        for (int i = 0; i < 60; i++)
            Touch($"cleaner-old-{i:00}.log", now.AddHours(-i));

        using var logger = new FileLogger(_dir, LogLevel.Info, keepCount: 50, keepDays: 365);
        logger.Log(LogLevel.Info, "hei");

        Assert.True(File.Exists(logger.FilePath));
        var logs = Directory.GetFiles(_dir, "cleaner-*.log");
        Assert.Equal(51, logs.Length);                                     // 50 säilytettyä + uusi
    }

    [Fact]
    public void Prune_survives_missing_directory()
    {
        var removed = FileLogger.PruneOldLogs(Path.Combine(_dir, "nope"));
        Assert.Equal(0, removed);
    }
}
