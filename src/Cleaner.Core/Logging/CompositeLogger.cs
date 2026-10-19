namespace Cleaner.Core.Logging;

/// <summary>Lähettää saman viestin usealle lokille (esim. konsoli + tiedosto).</summary>
public sealed class CompositeLogger : ICleanerLogger
{
    private readonly ICleanerLogger[] _loggers;

    public CompositeLogger(params ICleanerLogger[] loggers) => _loggers = loggers;

    public void Log(LogLevel level, string message)
    {
        foreach (var logger in _loggers)
            logger.Log(level, message);
    }
}
