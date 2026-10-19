using Cleaner.Core.Logging;

namespace Cleaner.Cli;

/// <summary>Värillinen konsoliloki. Debug-tason viestit näytetään vain --verbose-lipulla.</summary>
public sealed class ConsoleLogger : ICleanerLogger
{
    private readonly bool _verbose;

    public ConsoleLogger(bool verbose) => _verbose = verbose;

    public void Log(LogLevel level, string message)
    {
        if (level == LogLevel.Debug && !_verbose) return;

        var color = level switch
        {
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Debug => ConsoleColor.DarkGray,
            _ => Console.ForegroundColor
        };

        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = prev;
    }
}
