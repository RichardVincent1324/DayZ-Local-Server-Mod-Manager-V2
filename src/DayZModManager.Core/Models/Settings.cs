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
    /// active preset's profiles folder are all cleared on app startup once more
    /// than 20 of them have accumulated.
    /// </summary>
    public bool AutoCleanServerLogs { get; init; }

    /// <summary>
    /// The map whose preset is currently active. Empty until a map is applied.
    /// Preset-level configuration always comes from the active preset of this map.
    /// </summary>
    public string ActiveMap { get; init; } = string.Empty;

    /// <summary>
    /// The preset active for each map, keyed by map (mission folder) name. The
    /// active save's world state comes from the selected save of the active preset.
    /// </summary>
    public Dictionary<string, string> ActivePresets
    {
        get => _activePresets;
        init => _activePresets = value is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string> _activePresets = new(StringComparer.OrdinalIgnoreCase);

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
