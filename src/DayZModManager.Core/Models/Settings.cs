namespace DayZModManager.Core.Models;

using System.Text.Json.Serialization;

/// <summary>Persistent application settings.</summary>
public sealed record Settings
{
    public string WorkshopPath { get; init; } = string.Empty;

    public string ServerPath { get; init; } = string.Empty;

    /// <summary>
    /// The launch batch file, explicitly chosen by the user. Empty until one is
    /// picked: the app must never fall back to an implicit default file name. A
    /// bare file name is resolved relative to <see cref="ServerPath"/>; a rooted
    /// path is used as-is.
    /// </summary>
    public string BatchFile { get; init; } = string.Empty;

    /// <summary>
    /// When enabled, the DayZ server log files in the active map's profile folder
    /// (the DayZServer_x64_*.RPT, script_*.log, crash_*.log and warning_*.log files)
    /// are all deleted once 10+ DayZServer_x64_*.RPT and 10+ script_*.log files have
    /// accumulated. Cleanup runs once on app start.
    /// </summary>
    public bool AutoCleanServerLogs { get; init; }

    /// <summary>
    /// Full path to the launch batch file. A rooted <see cref="BatchFile"/> is
    /// used directly; otherwise it is combined with <see cref="ServerPath"/>. Empty
    /// while no batch file has been chosen. Derived, so it is not persisted.
    /// </summary>
    [JsonIgnore]
    public string BatFilePath =>
        string.IsNullOrWhiteSpace(BatchFile)
            ? string.Empty
            : Path.IsPathRooted(BatchFile) ? BatchFile : Path.Combine(ServerPath, BatchFile);
}
