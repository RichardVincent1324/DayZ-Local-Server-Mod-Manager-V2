namespace DayZModManager.Core.Models;

/// <summary>
/// Persistent application settings.
/// </summary>
public sealed record Settings
{
    public string WorkshopPath { get; init; } = string.Empty;

    public string ServerPath { get; init; } = string.Empty;

    /// <summary>
    /// The launch batch file. A bare file name is resolved relative to
    /// <see cref="ServerPath"/>; a rooted path is used as-is. Empty until the
    /// user selects one on first run.
    /// </summary>
    public string BatFileName { get; init; } = string.Empty;

    /// <summary>
    /// When enabled, DayZ server log files (<c>.rpt</c> and <c>.log</c>) in the
    /// active map's profile folder are all cleared on app startup once more than
    /// 20 of them have accumulated.
    /// </summary>
    public bool AutoCleanServerLogs { get; init; }

    /// <summary>
    /// Full path to the launch batch file. Empty while no batch file is selected;
    /// a rooted <see cref="BatFileName"/> is used directly, otherwise it is
    /// combined with <see cref="ServerPath"/>.
    /// </summary>
    public string BatFilePath =>
        string.IsNullOrWhiteSpace(BatFileName)
            ? string.Empty
            : Path.IsPathRooted(BatFileName)
                ? BatFileName
                : Path.Combine(ServerPath, BatFileName);
}
