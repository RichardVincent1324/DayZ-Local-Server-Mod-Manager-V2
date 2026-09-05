namespace DayZModManager.Core.Services;

/// <summary>
/// Name of the folder, relative to the server root, that hosts the junction for
/// every loaded mod. The same relative path is used in the batch file's mod list
/// (e.g. <c>ModList/@CF</c>), so it is the single source of truth for both the
/// filesystem layout and the launch script.
/// </summary>
public static class ModListFolder
{
    public const string Name = "ModList";

    /// <summary>Returns the server-root-relative mod path used in the batch file.</summary>
    public static string Entry(string modName) => $"{Name}/{modName}";
}
