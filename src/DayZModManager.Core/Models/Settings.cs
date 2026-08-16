namespace DayZModManager.Core.Models;

/// <summary>
/// Persistent application settings, with a schema version to support migrations.
/// </summary>
public sealed record Settings
{
    public const int CurrentSchemaVersion = 2;

    public string WorkshopPath { get; init; } = string.Empty;

    public string ServerPath { get; init; } = string.Empty;

    /// <summary>
    /// The launch batch file. A bare file name is resolved relative to
    /// <see cref="ServerPath"/>; a rooted path is used as-is.
    /// </summary>
    public string BatFileName { get; init; } = "LocalServer.example.bat";

    /// <summary>
    /// Optional override for the directory where configuration data files are
    /// stored. When empty, the directory is derived from <see cref="ServerPath"/>
    /// (a per-server subfolder) or the legacy AppData location.
    /// </summary>
    public string DataDirectory { get; init; } = string.Empty;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Full path to the launch batch file. A rooted <see cref="BatFileName"/> is
    /// used directly; otherwise it is combined with <see cref="ServerPath"/>.
    /// </summary>
    public string BatFilePath => Path.IsPathRooted(BatFileName) ? BatFileName : Path.Combine(ServerPath, BatFileName);
}
