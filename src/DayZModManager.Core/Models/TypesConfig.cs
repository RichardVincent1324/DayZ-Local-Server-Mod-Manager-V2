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
    public Dictionary<string, MapTypesConfig> Maps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentMap { get; set; } = string.Empty;
}

/// <summary>Types configuration for a single map.</summary>
public sealed class MapTypesConfig
{
    public List<ModTypesEntry> Mods { get; set; } = new();
}

/// <summary>
/// Types configuration for a single mod. <see cref="GeneratedFiles"/> are files
/// the manager copied into the mission folder and therefore owns; they can be
/// safely cleaned up. <see cref="SourceFiles"/> are the original mod XML files.
/// </summary>
public sealed class ModTypesEntry
{
    public string ModName { get; set; } = string.Empty;

    public List<string> SourceFiles { get; set; } = new();

    public List<string> GeneratedFiles { get; set; } = new();
}
