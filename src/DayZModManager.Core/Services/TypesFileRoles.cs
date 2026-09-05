namespace DayZModManager.Core.Services;

/// <summary>
/// Classifies a mod type file by the role it plays in the DayZ economy. A mod's
/// active types configuration may contain at most one "types" file and one
/// "spawnabletypes" file; extra candidates (e.g. a mod shipping Casual and
/// Hardcore variants) are mutually exclusive alternatives within their role.
/// </summary>
public static class TypesFileRoles
{
    public const string Types = "types";

    public const string Spawnabletypes = "spawnabletypes";

    /// <summary>
    /// Returns the role for a type file name. A file whose name contains
    /// "spawnable" (e.g. spawnabletypes.xml, casual_spawnabletypes.xml) is a
    /// spawnabletypes file; anything else is a regular types file.
    /// </summary>
    public static string RoleOf(string fileName) =>
        IsSpawnable(fileName) ? Spawnabletypes : Types;

    public static bool IsSpawnable(string fileName) =>
        fileName.Contains("spawnable", StringComparison.OrdinalIgnoreCase);
}
