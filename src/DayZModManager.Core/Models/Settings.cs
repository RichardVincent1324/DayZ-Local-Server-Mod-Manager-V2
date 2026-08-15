namespace DayZModManager.Core.Models;

/// <summary>
/// Persistent application settings, with a schema version to support migrations.
/// </summary>
public sealed record Settings
{
    public const int CurrentSchemaVersion = 1;

    public string WorkshopPath { get; init; } = string.Empty;

    public string ServerPath { get; init; } = string.Empty;

    public string BatFileName { get; init; } = "LocalServer.example.bat";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Full path to the launch batch file, derived from <see cref="ServerPath"/> and <see cref="BatFileName"/>.</summary>
    public string BatFilePath => Path.Combine(ServerPath, BatFileName);
}
