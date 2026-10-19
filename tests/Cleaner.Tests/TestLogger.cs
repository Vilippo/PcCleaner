using Cleaner.Core.Logging;

namespace Cleaner.Tests;

/// <summary>Kerää lokiviestit listaan, jotta testit voivat tarkistaa ne.</summary>
public sealed class TestLogger : ICleanerLogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();
    public void Log(LogLevel level, string message) => Entries.Add((level, message));
}
