namespace DayZModManager.Core.Models;

/// <summary>
/// Persistent per-map "types" configuration.
///
/// Maps are keyed by their mission folder name (e.g. "dayzOffline.chernarusplus")
/// rather than a full path, so the configuration is portable across machines.
/// The full path is resolved at runtime via map discovery.
/// </summary>
public sealed class TypesConfig
{
    private Dictionary<string, MapTypesConfig> _maps = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, MapTypesConfig> Maps
    {
        get => _maps;
        // System.Text.Json assigns a new dictionary (and a JSON null) directly, so
        // coerce null and restore the case-insensitive comparer here.
        set => _maps = value is null
            ? new Dictionary<string, MapTypesConfig>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, MapTypesConfig>(value, StringComparer.OrdinalIgnoreCase);
    }

    public string CurrentMap { get; set; } = string.Empty;
}

/// <summary>Types configuration for a single map.</summary>
public sealed class MapTypesConfig
{
    private List<ModTypesEntry> _mods = new();

    public List<ModTypesEntry> Mods
    {
        get => _mods;
        set => _mods = value ?? new List<ModTypesEntry>();
    }
}

/// <summary>
/// Types configuration for a single mod. <see cref="GeneratedFiles"/> are files
/// the manager copied into the mission folder and therefore owns; they can be
/// safely cleaned up. <see cref="SourceFiles"/> are the original mod XML files.
/// </summary>
public sealed class ModTypesEntry
{
    public string ModName { get; set; } = string.Empty;

    private List<string> _sourceFiles = new();

    public List<string> SourceFiles
    {
        get => _sourceFiles;
        set => _sourceFiles = value ?? new List<string>();
    }

    private List<string> _generatedFiles = new();

    public List<string> GeneratedFiles
    {
        get => _generatedFiles;
        set => _generatedFiles = value ?? new List<string>();
    }

    private Dictionary<string, string> _fileRoles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The economy role ("types" / "spawnabletypes") each generated file was
    /// copied under, keyed by generated leaf name. Stored explicitly so a
    /// keyword in the mod name or a source directory cannot change a file's
    /// role. Entries written before role storage lack this map and fall back to
    /// the filename heuristic.
    /// </summary>
    public Dictionary<string, string> FileRoles
    {
        get => _fileRoles;
        // System.Text.Json assigns a new dictionary (and a JSON null) directly, so
        // coerce null and restore the case-insensitive comparer here.
        set => _fileRoles = value is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
    }
}
