using System.Collections.ObjectModel;
using Cleaner.Core.Logging;
using Microsoft.UI.Dispatching;

namespace Cleaner.UI.Services;

/// <summary>
/// Näyttää lokirivit käyttöliittymässä. Moduulit lokittavat taustasäikeistä,
/// joten rivit siirretään UI-säikeeseen DispatcherQueue-jonon kautta.
/// </summary>
public sealed class UiLogger : ICleanerLogger
{
    private const int MaxLines = 300;
    private readonly DispatcherQueue _dispatcher;
    private readonly LogLevel _minLevel;

    public ObservableCollection<string> Lines { get; } = new();

    public UiLogger(DispatcherQueue dispatcher, LogLevel minLevel = LogLevel.Info)
    {
        _dispatcher = dispatcher;
        _minLevel = minLevel;
    }

    public void Log(LogLevel level, string message)
    {
        if (level < _minLevel) return;
        var line = $"{DateTime.Now:HH:mm:ss} {Tag(level)} {message}";
        _dispatcher.TryEnqueue(() =>
        {
            Lines.Add(line);
            while (Lines.Count > MaxLines) Lines.RemoveAt(0);
        });
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Error => "VIRHE",
        LogLevel.Warning => "VAROITUS",
        LogLevel.Debug => "debug",
        _ => "info"
    };
}
