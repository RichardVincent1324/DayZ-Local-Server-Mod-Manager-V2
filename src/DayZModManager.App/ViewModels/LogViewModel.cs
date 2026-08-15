using System.Collections.ObjectModel;

namespace DayZModManager.App.ViewModels;

/// <summary>Severity of a log line, used to drive its display colour.</summary>
public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>A single log line.</summary>
public sealed record LogEntry(string Message, LogLevel Level);

/// <summary>Collects log lines for the UI. Observable; must be mutated on the UI thread.</summary>
public sealed class LogViewModel
{
    private const int MaxEntries = 500;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public void Info(string message) => Add(message, LogLevel.Info);

    public void Success(string message) => Add(message, LogLevel.Success);

    public void Warning(string message) => Add(message, LogLevel.Warning);

    public void Error(string message) => Add(message, LogLevel.Error);

    private void Add(string message, LogLevel level)
    {
        Entries.Add(new LogEntry($"[{DateTime.Now:HH:mm:ss}] {message}", level));

        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }
    }
}
