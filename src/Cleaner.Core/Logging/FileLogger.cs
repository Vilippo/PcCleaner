namespace Cleaner.Core.Logging;

/// <summary>
/// Kirjoittaa lokin tekstitiedostoon, oletuksena %LOCALAPPDATA%\PcCleaner\logs\.
/// Säieturvallinen lukon avulla; kirjoitusmäärät ovat pieniä.
/// </summary>
public sealed class FileLogger : ICleanerLogger, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private readonly LogLevel _minLevel;

    public string FilePath { get; }

    /// <summary>Montako uusinta lokia säilytetään siivouksessa.</summary>
    public const int DefaultKeepCount = 50;

    /// <summary>Tätä vanhemmat lokit poistetaan, vaikka määrä olisi pieni.</summary>
    public const int DefaultKeepDays = 30;

    public FileLogger(string? directory = null, LogLevel minLevel = LogLevel.Info,
        int keepCount = DefaultKeepCount, int keepDays = DefaultKeepDays)
    {
        _minLevel = minLevel;
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PcCleaner", "logs");
        Directory.CreateDirectory(directory);

        PruneOldLogs(directory, keepCount, keepDays);

        FilePath = Path.Combine(directory, $"cleaner-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
        _writer = new StreamWriter(FilePath, append: true) { AutoFlush = true };
    }

    /// <summary>
    /// Poistaa vanhat cleaner-*.log-tiedostot: säilyttää enintään <paramref name="keepCount"/> uusinta
    /// ja poistaa kaikki yli <paramref name="keepDays"/> päivää vanhat. Käytössä olevat tiedostot ohitetaan.
    /// Palauttaa poistettujen määrän.
    /// </summary>
    public static int PruneOldLogs(string directory, int keepCount = DefaultKeepCount, int keepDays = DefaultKeepDays)
    {
        int removed = 0;
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-keepDays);
            var files = new DirectoryInfo(directory)
                .EnumerateFiles("cleaner-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            for (int i = 0; i < files.Count; i++)
            {
                if (i < keepCount && files[i].LastWriteTimeUtc >= cutoff) continue;
                try { files[i].Delete(); removed++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception)
        {
            // Siivous ei saa estää lokituksen käynnistymistä.
        }
        return removed;
    }

    public void Log(LogLevel level, string message)
    {
        if (level < _minLevel) return;
        lock (_lock)
        {
            _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level,-7}] {message}");
        }
    }

    public void Dispose() => _writer.Dispose();
}
