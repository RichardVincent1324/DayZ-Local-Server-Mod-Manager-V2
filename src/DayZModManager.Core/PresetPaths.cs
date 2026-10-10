namespace DayZModManager.Core;

/// <summary>
/// Well-known paths for the preset-driven data model. A preset is one complete,
/// independent DayZ server environment stored under
/// <c>&lt;dataDirectory&gt;\Presets\&lt;mapName&gt;\&lt;presetName&gt;</c> and owning its
/// own server configuration, mod order, types configuration, its generated type
/// files, profiles and world saves. This type is the single source of truth for that layout, in
/// the same spirit as <see cref="AppPaths"/> and
/// <c>DayZModManager.Core.Services.ModListFolder</c>.
/// </summary>
public static class PresetPaths
{
    /// <summary>Folder, under the data directory, that hosts every preset.</summary>
    public const string PresetsDirectoryName = "Presets";

    /// <summary>Reserved name of the automatically created per-map default preset.</summary>
    public const string DefaultPresetName = "__default_preset__";

    /// <summary>Folder inside a preset that holds its generated types XML files.</summary>
    public const string TypeFilesDirectoryName = "type_files";

    /// <summary>Folder inside a preset that holds its DayZ server profile data.</summary>
    public const string ProfilesDirectoryName = "profiles";

    /// <summary>Folder inside a preset that holds its world saves.</summary>
    public const string SavesDirectoryName = "saves";

    /// <summary>Root folder containing every map's presets.</summary>
    public static string PresetsRoot(string dataDirectory) =>
        Path.Combine(dataDirectory, PresetsDirectoryName);

    /// <summary>Folder containing every preset for a single map.</summary>
    public static string MapRoot(string dataDirectory, string mapName) =>
        Path.Combine(PresetsRoot(dataDirectory), mapName);

    /// <summary>Folder for a single preset.</summary>
    public static string PresetFolder(string dataDirectory, string mapName, string presetName) =>
        Path.Combine(MapRoot(dataDirectory, mapName), presetName);

    /// <summary>Preset metadata file path (<c>preset-meta.json</c>).</summary>
    public static string PresetMetaPath(string dataDirectory, string mapName, string presetName) =>
        Path.Combine(PresetFolder(dataDirectory, mapName, presetName), ConfigFileNames.PresetMeta);

    /// <summary>Preset server configuration file path (<c>serverDZ.cfg</c>).</summary>
    public static string ServerConfigPath(string dataDirectory, string mapName, string presetName) =>
        Path.Combine(PresetFolder(dataDirectory, mapName, presetName), ConfigFileNames.ServerConfig);

    /// <summary>Preset mod order file path (<c>mod_order.json</c>).</summary>
    public static string ModOrderPath(string dataDirectory, string mapName, string presetName) =>
        Path.Combine(PresetFolder(dataDirectory, mapName, presetName), ConfigFileNames.ModOrder);

    /// <summary>Preset types configuration file path (<c>types_config.json</c>).</summary>
    public static string TypesConfigPath(string dataDirectory, string mapName, string presetName) =>
        Path.Combine(PresetFolder(dataDirectory, mapName, presetName), ConfigFileNames.TypesConfig);

    /// <summary>Preset type_files folder path.</summary>
    public static string TypeFilesFolder(string dataDirectory, string mapName, string presetName) =>
        Path.Combine(PresetFolder(dataDirectory, mapName, presetName), TypeFilesDirectoryName);

    /// <summary>Preset profiles folder path.</summary>
    public static string ProfilesFolder(string dataDirectory, string mapName, string presetName) =>
        Path.Combine(PresetFolder(dataDirectory, mapName, presetName), ProfilesDirectoryName);

    /// <summary>Preset saves folder path.</summary>
    public static string SavesFolder(string dataDirectory, string mapName, string presetName) =>
        Path.Combine(PresetFolder(dataDirectory, mapName, presetName), SavesDirectoryName);

    /// <summary>True when <paramref name="presetName"/> is the reserved default preset.</summary>
    public static bool IsDefaultPreset(string? presetName) =>
        string.Equals(presetName, DefaultPresetName, StringComparison.Ordinal);

    /// <summary>
    /// Returns a value suitable for a launch-batch variable: the path relative to
    /// the server root when the target lives under it, otherwise the absolute path
    /// unchanged.
    /// </summary>
    public static string RelativeToServer(string serverPath, string target)
    {
        if (string.IsNullOrWhiteSpace(serverPath) || string.IsNullOrWhiteSpace(target))
        {
            return target;
        }

        string relative = Path.GetRelativePath(serverPath, target);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? target
            : relative;
    }
}
