namespace DayZModManager.Core.Models;

/// <summary>
/// Persistent application settings, with a schema version to support migrations.
/// </summary>
public sealed record Settings
{
    public const int CurrentSchemaVersion = 3;

    public string WorkshopPath { get; init; } = string.Empty;

    public string ServerPath { get; init; } = string.Empty;

    /// <summary>
    /// The launch batch file. A bare file name is resolved relative to
    /// <see cref="ServerPath"/>; a rooted path is used as-is.
    /// </summary>
    public string BatFileName { get; init; } = "LocalServer.example.bat";

    /// <summary>
    /// When enabled, old DayZ server log files (the DayZServer_x64_*.RPT and
    /// script_*.log files in the active map's profile folder) are pruned to the
    /// three most recent of each. Cleanup runs on app start, after an Apply, and
    /// before starting the server.
    /// </summary>
    public bool AutoCleanServerLogs { get; init; }

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Full path to the launch batch file. A rooted <see cref="BatFileName"/> is
    /// used directly; otherwise it is combined with <see cref="ServerPath"/>.
    /// </summary>
        public string BatFilePath => Path.IsPathRooted(BatFileName) ? BatFileName : Path.Combine(ServerPath, BatFileName);
}
