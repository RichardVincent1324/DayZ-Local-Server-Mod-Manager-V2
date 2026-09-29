namespace DayZModManager.Core.Models;

/// <summary>
/// Configuration snapshot stored alongside a progress save (meta.json). A DayZ
/// storage folder is tied to the mod list (including its order) and the active
/// type files, so this records the configuration the world was saved under.
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
    public List<string> ModList
    {
        get => _modList;
        set => _modList = value ?? new List<string>();
    }

    /// <summary>
    /// Active generated type-file leaf names for the map (in cfgeconomycore.xml
    /// order: regular types before spawnabletypes). Order is informational;
    /// compatibility comparisons treat this as a set.
    /// </summary>
    public List<string> TypesFiles
    {
        get => _typesFiles;
        set => _typesFiles = value ?? new List<string>();
    }

    private List<string> _modList = new();

    private List<string> _typesFiles = new();

    /// <summary>
    /// The map's types configuration (mods and their source/generated files) at
    /// save time. Loading a save restores this into <c>types_config.json</c> so
    /// the config always mirrors the live <c>db\ModTypes</c> folder. Null only
    /// when the snapshot is missing it; such a save skips types restoration.
    /// </summary>
    public MapTypesConfig? TypesConfig { get; set; }
}
