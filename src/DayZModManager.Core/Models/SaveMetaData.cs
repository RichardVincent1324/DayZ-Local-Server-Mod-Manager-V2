namespace DayZModManager.Core.Models;

/// <summary>
/// Configuration snapshot stored alongside a progress save (meta.json). A DayZ
/// storage folder is tied to the mod list (including its order) and the active
/// type files, so this records the configuration the world was saved under so a
/// later load can warn when it no longer matches.
/// </summary>
public sealed class SaveMetaData
{
    /// <summary>Mission folder name the save belongs to (e.g. "dayzOffline.chernarusplus").</summary>
    public string Map { get; set; } = string.Empty;

    /// <summary>Leaf name of the nested storage folder (e.g. "storage_1").</summary>
    public string StorageFolder { get; set; } = string.Empty;

    /// <summary>When the save was created, in UTC.</summary>
    public DateTime SavedAtUtc { get; set; }

    /// <summary>Ordered list of loaded mods at save time.</summary>
    public List<string> ModList { get; set; } = new();

    /// <summary>
    /// Active generated type-file leaf names for the map (in cfgeconomycore.xml
    /// order: regular types before spawnabletypes). Order is informational;
    /// compatibility comparisons treat this as a set.
    /// </summary>
    public List<string> TypesFiles { get; set; } = new();

    /// <summary>
    /// The map's types configuration (which mod owns which generated file) at save
    /// time. Restoring it alongside the physical snapshot keeps the manager's
    /// tracking config and cfgeconomycore.xml consistent with the restored files.
    /// Null for saves created before this snapshot existed (legacy saves), whose
    /// ModTypes files cannot be restored automatically.
    /// </summary>
    public MapTypesConfig? Types { get; set; }
}
