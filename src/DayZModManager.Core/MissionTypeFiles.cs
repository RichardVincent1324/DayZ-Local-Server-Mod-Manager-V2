namespace DayZModManager.Core;

/// <summary>
/// Well-known names and paths for the mission-local junction that exposes the
/// active preset's generated type files to the game and to tools that only scan
/// <c>mpmissions\&lt;mapName&gt;\db</c> (for example VPPAdminTool).
/// <para>
/// There is one stable junction per mission,
/// <c>&lt;serverPath&gt;\mpmissions\&lt;mapName&gt;\db\DZMMV2_Types</c>, whose
/// target is re-pointed when the active preset changes. The junction name and
/// the source folder name (<c>type_files</c>) are independent.
/// </para>
/// </summary>
public static class MissionTypeFiles
{
    /// <summary>Folder inside a mission's <c>db</c> directory that hosts the junction.</summary>
    public const string JunctionName = "DZMMV2_Types";

    /// <summary>Mission-relative folder value written to the managed CE block.</summary>
    public const string CeFolder = "db/" + JunctionName;

    /// <summary>Returns a map's mission directory.</summary>
    public static string MissionPath(string serverPath, string mapName) =>
        Path.Combine(serverPath, "mpmissions", mapName);

    /// <summary>Returns the absolute path of a map's stable type-files junction.</summary>
    public static string JunctionPath(string missionPath) =>
        Path.Combine(missionPath, "db", JunctionName);
}
