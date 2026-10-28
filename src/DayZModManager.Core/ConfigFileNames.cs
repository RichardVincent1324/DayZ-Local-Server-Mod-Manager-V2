namespace DayZModManager.Core;

/// <summary>Well-known configuration file names used by all stores.</summary>
public static class ConfigFileNames
{
    public const string Settings = "settings.json";
    public const string ModOrder = "mod_order.json";
    public const string TypesConfig = "types_config.json";

    /// <summary>Preset-level metadata file (holds the preset's dedicated instanceId).</summary>
    public const string PresetMeta = "preset-meta.json";

    /// <summary>Save-level metadata file (identifies the save and its parent preset).</summary>
    public const string SaveMeta = "save-meta.json";

    /// <summary>DayZ server configuration file, owned by each preset.</summary>
    public const string ServerConfig = "serverDZ.cfg";
}
