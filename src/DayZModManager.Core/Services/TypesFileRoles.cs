namespace DayZModManager.Core.Services;

/// <summary>
/// The two economy roles a type file can be assigned. The filesystem determines
/// which files exist; <c>type_files.json</c> stores only this filename-to-role
/// mapping.
/// </summary>
public static class TypesFileRoles
{
    public const string Types = "types";

    public const string Spawnabletypes = "spawnabletypes";

    /// <summary>
    /// Maps a stored value to one of the two supported roles. Returns false for
    /// null, empty, or any other value (which callers treat as unassigned).
    /// </summary>
    public static bool TryNormalize(string? stored, out string normalized)
    {
        if (string.Equals(stored, Spawnabletypes, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Spawnabletypes;
            return true;
        }

        if (string.Equals(stored, Types, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Types;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    /// <summary>Maps a persisted economy type value back to a role; anything unknown is a types file.</summary>
    public static string NormalizeEconomyType(string? stored) =>
        string.Equals(stored, Spawnabletypes, StringComparison.OrdinalIgnoreCase) ? Spawnabletypes : Types;
}
