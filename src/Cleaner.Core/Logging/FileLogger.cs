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

    public FileLogger(string? directory = null, LogLevel minLevel = LogLevel.Info)
    {
        _minLevel = minLevel;
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PcCleaner", "logs");
        Directory.CreateDirectory(directory);

        FilePath = Path.Combine(directory, $"cleaner-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
        _writer = new StreamWriter(FilePath, append: true) { AutoFlush = true };
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
