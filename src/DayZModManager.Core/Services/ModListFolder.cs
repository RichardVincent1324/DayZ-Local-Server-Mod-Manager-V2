using System.Globalization;

namespace DayZModManager.Core.Services;

/// <summary>
/// Name of the folder, relative to the server root, that hosts the junctions for
/// every loaded mod. Each preset gets its own subfolder, keyed by the preset's
/// dedicated <c>instanceId</c>, so switching presets never recreates or deletes
/// another preset's junctions. The same server-relative path is used in the batch
/// file's mod list (e.g. <c>ModList/1/@CF</c>), so this type is the single source
/// of truth for both the filesystem layout and the launch script.
/// </summary>
public static class ModListFolder
{
    /// <summary>Root folder, relative to the server root, containing per-preset junction folders.</summary>
    public const string Name = "ModList";

    /// <summary>Returns the preset key used as the junction subfolder name.</summary>
    public static string PresetKey(int instanceId) =>
        instanceId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Returns the absolute path of a preset's junction folder.</summary>
    public static string Directory(string serverPath, string presetKey) =>
        Path.Combine(serverPath, Name, presetKey);

    /// <summary>Returns the server-root-relative mod path used in the batch file.</summary>
    public static string Entry(string presetKey, string modName) =>
        $"{Name}/{presetKey}/{modName}";
}
