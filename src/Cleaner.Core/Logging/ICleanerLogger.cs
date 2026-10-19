namespace Cleaner.Core.Logging;

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// Kevyt lokirajapinta. Jokainen poisto kirjataan, jotta jälkikäteen näkee mitä tapahtui.
/// </summary>
public interface ICleanerLogger
{
    void Log(LogLevel level, string message);

    void Debug(string message) => Log(LogLevel.Debug, message);
    void Info(string message) => Log(LogLevel.Info, message);
    void Warning(string message) => Log(LogLevel.Warning, message);
    void Error(string message) => Log(LogLevel.Error, message);
}
