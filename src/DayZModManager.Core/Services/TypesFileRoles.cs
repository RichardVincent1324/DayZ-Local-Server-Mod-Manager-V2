namespace DayZModManager.Core.Services;

/// <summary>The economy role a selected type file is copied under.</summary>
public enum TypesFileRole
{
    Types,
    SpawnableTypes,
}

/// <summary>
/// Classifies a mod type file by the role it plays in the DayZ economy. A file
/// whose name carries the "type"/"spawnable" keyword is recognized and its role
/// follows from the name; unrecognized files get the role the user assigned.
/// </summary>
public static class TypesFileRoles
{
    public const string Types = "types";

    public const string Spawnabletypes = "spawnabletypes";

    public static bool IsSpawnable(string fileName) =>
        fileName.Contains("spawnable", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a file name carries an economy keyword ("type" or "spawnable").
    /// Recognized files are classified by <see cref="EffectiveRole"/> and cannot be
    /// overridden; unrecognized files let the user pick a role explicitly.
    /// </summary>
    public static bool IsRecognized(string fileName) =>
        fileName.Contains("type", StringComparison.OrdinalIgnoreCase)
        || fileName.Contains("spawnable", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the effective role for a file name, ignoring the requested role when recognized.</summary>
    public static TypesFileRole EffectiveRole(string fileName, TypesFileRole requested) =>
        IsRecognized(fileName)
            ? (IsSpawnable(fileName) ? TypesFileRole.SpawnableTypes : TypesFileRole.Types)
            : (requested == TypesFileRole.SpawnableTypes ? TypesFileRole.SpawnableTypes : TypesFileRole.Types);

    /// <summary>Maps a role to the economy type value written into cfgeconomycore.xml.</summary>
    public static string ToEconomyType(TypesFileRole role) =>
        role == TypesFileRole.SpawnableTypes ? Spawnabletypes : Types;

    /// <summary>Maps a persisted economy type value back to a role; anything unknown is a types file.</summary>
    public static string NormalizeEconomyType(string? stored) =>
        string.Equals(stored, Spawnabletypes, StringComparison.OrdinalIgnoreCase) ? Spawnabletypes : Types;

    /// <summary>Maps a persisted economy type value to a role.</summary>
    public static TypesFileRole ToRole(string? stored) =>
        string.Equals(NormalizeEconomyType(stored), Spawnabletypes, StringComparison.OrdinalIgnoreCase)
            ? TypesFileRole.SpawnableTypes
            : TypesFileRole.Types;
}

/// <summary>A user selection from the types-file picker: a source file and the role to copy it as.</summary>
public sealed record TypeFileSelection(string SourceFile, TypesFileRole Role);
