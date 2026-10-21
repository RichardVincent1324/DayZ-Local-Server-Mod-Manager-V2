using System.Text.Json.Serialization;

namespace DayZModManager.Core.Models;

/// <summary>
/// Persistent types configuration for one preset. A preset is scoped to a single
/// map, so the file holds only that map's mods; the map is implied by the preset
/// folder path (<c>Presets\&lt;map&gt;\&lt;preset&gt;</c>) and is not serialized.
/// </summary>
public sealed class TypesConfig
{
    private List<ModTypesEntry> _mods = new();

    public List<ModTypesEntry> Mods
    {
        get => _mods;
        set => _mods = value ?? new List<ModTypesEntry>();
    }

    /// <summary>
    /// The map whose preset is currently loaded. Runtime-only (never serialized);
    /// the preset folder is the persistent source of truth for the map.
    /// </summary>
    [JsonIgnore]
    public string CurrentMap { get; set; } = string.Empty;

    /// <summary>
    /// True when the active preset's <c>types_config.json</c> could not be read.
    /// Runtime-only: automatic saves and economy rewrites are skipped so the
    /// corrupt file is not replaced with an empty configuration.
    /// </summary>
    [JsonIgnore]
    public bool IsCorrupt { get; set; }
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
