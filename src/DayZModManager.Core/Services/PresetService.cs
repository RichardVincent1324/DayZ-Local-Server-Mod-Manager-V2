using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a preset operation.</summary>
public sealed record PresetResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>True when the operation succeeded but performed no work.</summary>
    public bool Informational { get; init; }
}

/// <summary>
/// Manages the preset hierarchy under <c>&lt;dataDirectory&gt;\Presets\&lt;map&gt;</c>.
/// A preset is one complete DayZ server environment: it owns a dedicated
/// <c>instanceId</c> (persisted in <c>preset-meta.json</c>), its own
/// <c>serverDZ.cfg</c>, mod order, types configuration, generated type files,
/// profiles and world saves. <c>__default_preset__</c> is a normal preset with a reserved name
/// that is auto-created for every map and cannot be renamed or deleted.
/// </summary>
public interface IPresetService
{
    /// <summary>True when <paramref name="presetName"/> is the reserved default preset.</summary>
    bool IsDefaultPreset(string? presetName);

    /// <summary>Returns the full folder path of a preset (whether or not it exists).</summary>
    string GetPresetFolder(string dataDirectory, string mapName, string presetName);

    /// <summary>
    /// True when a preset is usable: its folder, <c>preset-meta.json</c> and its
    /// <c>serverDZ.cfg</c> all exist. A bare folder (or one missing its metadata or
    /// server configuration) is not a preset this tool can activate.
    /// </summary>
    bool PresetExists(string dataDirectory, string mapName, string presetName);

    /// <summary>
    /// Returns a description of the first missing part of a preset (its folder,
    /// <c>preset-meta.json</c>, or <c>serverDZ.cfg</c>), or null when the preset is
    /// complete. Read-only: never creates or repairs anything, so callers can
    /// report the exact problem to the user without side effects.
    /// </summary>
    string? DescribeIncompletePreset(string dataDirectory, string mapName, string presetName);

    /// <summary>
    /// Resolves the launch environment for the active map and preset, healing the
    /// reserved default preset when it is the active one and aborting with a clear
    /// message when a named preset is incomplete. This is the single place preset
    /// completeness is decided. When <paramref name="mapName"/> is empty there is
    /// no active environment: the result has a null environment and no error.
    /// <para>
    /// <paramref name="dataDirectory"/> is where the resulting profile/config paths
    /// are anchored (the relocation target). <paramref name="presetLookupDirectory"/>
    /// is where the preset must exist and is healed - typically the current data
    /// directory, which may differ from the target during a relocation. It defaults
    /// to <paramref name="dataDirectory"/>.
    /// </para>
    /// </summary>
    EnvironmentResolution ResolveEnvironment(
        Settings settings, string dataDirectory, string mapName, string presetName,
        string? presetLookupDirectory = null);

    /// <summary>
    /// Returns the preset names for a map, with <c>__default_preset__</c> first and
    /// the remaining names sorted case-insensitively.
    /// </summary>
    IReadOnlyList<string> ListPresetNames(string dataDirectory, string mapName);

    /// <summary>
    /// Reads a preset's dedicated instance ID. The authoritative value lives in
    /// <c>preset-meta.json</c>; when that is missing or unreadable, the preset's
    /// own <c>serverDZ.cfg</c> (the value DayZ actually uses) is read as a
    /// fallback. Returns 0 when neither can supply a valid ID - never a made-up
    /// value that could collide with an existing world's storage slot.
    /// </summary>
    int ReadInstanceId(string dataDirectory, string mapName, string presetName);

    /// <summary>
    /// Allocates the next free instance ID for <paramref name="mapName"/> as
    /// <c>max(reserved) + 1</c>. Reserved IDs are every preset's ID across every
    /// map (IDs key the shared <c>ModList</c> junction folder, so they must be
    /// globally unique) plus every live <c>storage_&lt;id&gt;</c> folder already
    /// present in the map's mission folder. Reserving the live storage IDs ensures
    /// a new preset is never bound to a world this tool did not create.
    /// </summary>
    int AllocateInstanceId(string dataDirectory, string serverPath, string mapName);

    /// <summary>
    /// Creates <c>__default_preset__</c> for a map when it does not yet exist,
    /// seeding its <c>serverDZ.cfg</c> from the server root. A default preset whose
    /// folder exists but is missing its <c>serverDZ.cfg</c> is reported as a
    /// failure instead of being re-seeded. Idempotent.
    /// </summary>
    PresetResult EnsureDefaultPreset(string serverPath, string dataDirectory, string mapName);

    /// <summary>
    /// Creates a new named preset for a map with a fresh, dedicated instance ID and
    /// an empty profiles folder. Presets never share profile data.
    /// </summary>
    PresetResult CreatePreset(
        string serverPath, string dataDirectory, string mapName, string presetName);

    /// <summary>
    /// Creates a new preset as a copy of an existing preset's environment: its
    /// server configuration (with a fresh, dedicated instance ID), mod order, types
    /// configuration and generated type files. The copy may target the same map as
    /// the source or a different one; map-scoped settings (the <c>serverDZ.cfg</c>
    /// template and the types configuration's map key) are rewritten for the
    /// destination map. Profiles and saves are never copied.
    /// </summary>
    PresetResult DuplicatePreset(
        string serverPath, string dataDirectory,
        string sourceMapName, string sourcePresetName,
        string targetMapName, string newPresetName);

    /// <summary>Renames a user preset. The reserved default preset cannot be renamed.</summary>
    PresetResult RenamePreset(string dataDirectory, string mapName, string presetName, string newName);

    /// <summary>Deletes a user preset. The reserved default preset cannot be deleted.</summary>
    PresetResult DeletePreset(string dataDirectory, string mapName, string presetName);
}

public sealed class PresetService : IPresetService
{
    /// <summary>
    /// The naming rule for presets. A preset name becomes part of the folder path
    /// that the launch batch passes to the server as <c>%serverConfig%</c> /
    /// <c>%serverProfile%</c> (unquoted, because the DayZ Server does not accept a
    /// quoted <c>-config=</c> value), so spaces and shell-special characters would
    /// break the launch.
    /// </summary>
    public const string PresetNameRequirement =
        "Use only letters, digits, '.', '_', '-' or '+' (no spaces).";

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly IFileSystem _fileSystem;
    private readonly IServerConfigService _serverConfig;

    public PresetService(IFileSystem fileSystem, IServerConfigService serverConfig)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _serverConfig = serverConfig ?? throw new ArgumentNullException(nameof(serverConfig));
    }

    public bool IsDefaultPreset(string? presetName) => PresetPaths.IsDefaultPreset(presetName);

    public string GetPresetFolder(string dataDirectory, string mapName, string presetName) =>
        PresetPaths.PresetFolder(dataDirectory, mapName, presetName);

    public bool PresetExists(string dataDirectory, string mapName, string presetName) =>
        _fileSystem.DirectoryExists(PresetPaths.PresetFolder(dataDirectory, mapName, presetName))
        && _fileSystem.FileExists(PresetPaths.PresetMetaPath(dataDirectory, mapName, presetName))
        && _fileSystem.FileExists(PresetPaths.ServerConfigPath(dataDirectory, mapName, presetName));

    public string? DescribeIncompletePreset(string dataDirectory, string mapName, string presetName)
    {
        string folder = PresetPaths.PresetFolder(dataDirectory, mapName, presetName);
        if (!_fileSystem.DirectoryExists(folder))
        {
            return $"preset folder is missing ({folder})";
        }

        string metaPath = PresetPaths.PresetMetaPath(dataDirectory, mapName, presetName);
        if (!_fileSystem.FileExists(metaPath))
        {
            return $"preset-meta.json is missing ({metaPath})";
        }

        string configPath = PresetPaths.ServerConfigPath(dataDirectory, mapName, presetName);
        if (!_fileSystem.FileExists(configPath))
        {
            return $"serverDZ.cfg is missing ({configPath})";
        }

        return null;
    }

    public EnvironmentResolution ResolveEnvironment(
        Settings settings, string dataDirectory, string mapName, string presetName,
        string? presetLookupDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return new EnvironmentResolution(null, null);
        }

        string activePreset = string.IsNullOrWhiteSpace(presetName)
            ? PresetPaths.DefaultPresetName
            : presetName;
        string lookupDirectory = string.IsNullOrWhiteSpace(presetLookupDirectory)
            ? dataDirectory
            : presetLookupDirectory;

        if (!PresetExists(lookupDirectory, mapName, activePreset))
        {
            if (IsDefaultPreset(activePreset))
            {
                // The tool-managed default preset is healed from the server root.
                PresetResult healed = EnsureDefaultPreset(settings.ServerPath, lookupDirectory, mapName);
                if (!healed.Success)
                {
                    return EnvironmentResolution.Failed(healed.Message);
                }
            }

            if (!PresetExists(lookupDirectory, mapName, activePreset))
            {
                return EnvironmentResolution.Failed(
                    $"The active preset \"{activePreset}\" for {mapName} is incomplete " +
                    $"(its serverDZ.cfg is missing); the map was not applied. Restore it or select another preset.");
            }
        }

        var environment = new ServerEnvironment
        {
            MapName = mapName,
            PresetName = activePreset,
            PresetFolder = PresetPaths.PresetFolder(dataDirectory, mapName, activePreset),
            ServerProfile = PresetPaths.RelativeToServer(
                settings.ServerPath,
                PresetPaths.ProfilesFolder(dataDirectory, mapName, activePreset)),
            ServerConfig = PresetPaths.RelativeToServer(
                settings.ServerPath,
                PresetPaths.ServerConfigPath(dataDirectory, mapName, activePreset)),
            ModListKey = ModListFolder.PresetKey(ReadInstanceId(lookupDirectory, mapName, activePreset)),
        };

        return EnvironmentResolution.Resolved(environment);
    }

    public IReadOnlyList<string> ListPresetNames(string dataDirectory, string mapName)
    {
        string mapRoot = PresetPaths.MapRoot(dataDirectory, mapName);
        if (!_fileSystem.DirectoryExists(mapRoot))
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        if (_fileSystem.DirectoryExists(PresetPaths.PresetFolder(dataDirectory, mapName, PresetPaths.DefaultPresetName)))
        {
            names.Add(PresetPaths.DefaultPresetName);
        }

        names.AddRange(_fileSystem
            .GetDirectories(mapRoot)
            .Where(name => !PresetPaths.IsDefaultPreset(name))
            .Where(name => !name.StartsWith(".", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

        return names;
    }

    public int ReadInstanceId(string dataDirectory, string mapName, string presetName)
    {
        string metaPath = PresetPaths.PresetMetaPath(dataDirectory, mapName, presetName);
        ConfigLoadResult<PresetMetaData> loaded = ConfigJson.Read<PresetMetaData>(_fileSystem, metaPath);
        if (loaded.Status == ConfigLoadStatus.Success && loaded.Value is { InstanceId: > 0 })
        {
            return loaded.Value.InstanceId;
        }

        // The metadata is missing or corrupt. Fall back to the preset's own
        // serverDZ.cfg, which is the value DayZ reads to locate storage_<id>.
        // This keeps the preset attached to its real world instead of silently
        // claiming storage_1 and colliding with the default preset.
        string configPath = PresetPaths.ServerConfigPath(dataDirectory, mapName, presetName);
        int? fromConfig = _serverConfig.TryReadInstanceId(configPath);
        if (fromConfig is > 0)
        {
            return fromConfig.Value;
        }

        // Neither metadata nor config can identify the preset: report 0 rather
        // than inventing an ID. 0 is not a valid storage slot, so no pre-existing
        // world can be mistaken for this preset's.
        return 0;
    }

    public int AllocateInstanceId(string dataDirectory, string serverPath, string mapName) =>
        ReserveInstanceId(dataDirectory, serverPath, mapName, preferred: null);

    /// <summary>
    /// Reserves every in-use instance ID (presets across all maps plus the live
    /// storage folders of <paramref name="mapName"/>) and returns a free one,
    /// honoring <paramref name="preferred"/> when it is valid and unused.
    /// </summary>
    private int ReserveInstanceId(string dataDirectory, string serverPath, string mapName, int? preferred)
    {
        var presetIds = new HashSet<int>();
        string root = PresetPaths.PresetsRoot(dataDirectory);
        if (_fileSystem.DirectoryExists(root))
        {
            foreach (string map in _fileSystem.GetDirectories(root))
            {
                string mapRoot = Path.Combine(root, map);
                if (!_fileSystem.DirectoryExists(mapRoot))
                {
                    continue;
                }

                foreach (string preset in _fileSystem.GetDirectories(mapRoot))
                {
                    int id = ReadInstanceId(dataDirectory, map, preset);
                    if (id > 0)
                    {
                        presetIds.Add(id);
                    }
                }
            }
        }

        // Live storage folders on this map are reserved so a fresh preset is never
        // bound to a world this tool did not create. The default preset is exempt
        // for its preferred ID below: adopting the server root's own instance ID
        // is exactly how an existing world stays attached to the default preset.
        var storageIds = new HashSet<int>(
            StorageFolders.ListInstanceIds(_fileSystem, serverPath, mapName).Where(id => id > 0));

        if (preferred is > 0 && !presetIds.Contains(preferred.Value))
        {
            return preferred.Value;
        }

        int max = 0;
        foreach (int id in presetIds)
        {
            max = Math.Max(max, id);
        }

        foreach (int id in storageIds)
        {
            max = Math.Max(max, id);
        }

        return max + 1;
    }

    public PresetResult EnsureDefaultPreset(string serverPath, string dataDirectory, string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return Failure("A map is required to create the default preset.");
        }

        string presetFolder = PresetPaths.PresetFolder(dataDirectory, mapName, PresetPaths.DefaultPresetName);
        if (_fileSystem.DirectoryExists(presetFolder))
        {
            EnsureSubfolders(dataDirectory, mapName, PresetPaths.DefaultPresetName);

            bool hasMeta = _fileSystem.FileExists(
                PresetPaths.PresetMetaPath(dataDirectory, mapName, PresetPaths.DefaultPresetName));
            bool hasConfig = _fileSystem.FileExists(
                PresetPaths.ServerConfigPath(dataDirectory, mapName, PresetPaths.DefaultPresetName));

            if (hasMeta && hasConfig)
            {
                return Success($"Default preset already exists for {mapName}.");
            }

            return RepairDefaultPreset(serverPath, dataDirectory, mapName, hasMeta, hasConfig);
        }

        return CreatePresetCore(serverPath, dataDirectory, mapName, PresetPaths.DefaultPresetName);
    }

    /// <summary>
    /// Repairs a default preset whose <c>preset-meta.json</c> went missing,
    /// preserving its instance ID (and world slot). A missing <c>serverDZ.cfg</c>
    /// is reported as a failure instead of being re-seeded, so the user restores it
    /// themselves. Never allocates a new ID when one can still be read from the
    /// preset's metadata or configuration.
    /// </summary>
    private PresetResult RepairDefaultPreset(
        string serverPath, string dataDirectory, string mapName, bool hasMeta, bool hasConfig)
    {
        // Never re-seed a missing serverDZ.cfg: a default preset that lost its
        // config is reported so the user can restore it (or reinstall), rather than
        // silently copying a config back in.
        if (!hasConfig)
        {
            string configPath = PresetPaths.ServerConfigPath(dataDirectory, mapName, PresetPaths.DefaultPresetName);
            return Failure(
                $"The default preset's serverDZ.cfg is missing ({configPath}). " +
                "restore it by deleting the __default_preset__ folder, and the program will generate a new one.");
        }

        // Only the metadata is missing; rebuild it, preserving the instance ID (and
        // therefore the world slot) the config already points at.
        int instanceId = ReadInstanceId(dataDirectory, mapName, PresetPaths.DefaultPresetName);
        if (instanceId <= 0)
        {
            int? preferred = _serverConfig.TryReadInstanceId(Path.Combine(serverPath, ConfigFileNames.ServerConfig));
            instanceId = ReserveInstanceId(dataDirectory, serverPath, mapName, preferred);
        }

        DateTime createdAt = hasMeta
            ? ReadCreatedAtUtc(dataDirectory, mapName, PresetPaths.DefaultPresetName)
            : DateTime.UtcNow;

        var meta = new PresetMetaData
        {
            InstanceId = instanceId,
            CreatedAtUtc = createdAt,
        };
        ConfigJson.Write(
            _fileSystem, PresetPaths.PresetMetaPath(dataDirectory, mapName, PresetPaths.DefaultPresetName), meta);

        return Success($"Repaired the default preset metadata for {mapName} (instanceId {instanceId}).");
    }

    /// <summary>Returns a preset's recorded creation time, or now when it has none.</summary>
    private DateTime ReadCreatedAtUtc(string dataDirectory, string mapName, string presetName)
    {
        ConfigLoadResult<PresetMetaData> loaded = ConfigJson.Read<PresetMetaData>(
            _fileSystem, PresetPaths.PresetMetaPath(dataDirectory, mapName, presetName));
        return loaded.Status == ConfigLoadStatus.Success && loaded.Value!.CreatedAtUtc != default
            ? loaded.Value.CreatedAtUtc
            : DateTime.UtcNow;
    }

    public PresetResult CreatePreset(
        string serverPath, string dataDirectory, string mapName, string presetName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return Failure("A map is required to create a preset.");
        }

        string? name = NormalizePresetName(presetName);
        if (name is null)
        {
            return Failure($"Invalid preset name. {PresetNameRequirement}");
        }

        if (PresetPaths.IsDefaultPreset(name))
        {
            return Failure("\"__default_preset__\" is reserved and is created automatically.");
        }

        if (_fileSystem.DirectoryExists(PresetPaths.PresetFolder(dataDirectory, mapName, name)))
        {
            return Failure($"A preset named \"{name}\" already exists for {mapName}.");
        }

        return CreatePresetCore(serverPath, dataDirectory, mapName, name);
    }

    public PresetResult DuplicatePreset(
        string serverPath, string dataDirectory,
        string sourceMapName, string sourcePresetName,
        string targetMapName, string newPresetName)
    {
        if (string.IsNullOrWhiteSpace(sourceMapName) || string.IsNullOrWhiteSpace(targetMapName))
        {
            return Failure("A source and destination map are required.");
        }

        string? name = NormalizePresetName(newPresetName);
        if (name is null)
        {
            return Failure($"Invalid preset name. {PresetNameRequirement}");
        }

        if (PresetPaths.IsDefaultPreset(name))
        {
            return Failure("\"__default_preset__\" is reserved and is created automatically.");
        }

        string sourceFolder = PresetPaths.PresetFolder(dataDirectory, sourceMapName, sourcePresetName);
        if (!_fileSystem.DirectoryExists(sourceFolder))
        {
            return Failure($"Preset \"{sourcePresetName}\" was not found.");
        }

        string targetFolder = PresetPaths.PresetFolder(dataDirectory, targetMapName, name);
        if (_fileSystem.DirectoryExists(targetFolder))
        {
            return Failure($"A preset named \"{name}\" already exists for {targetMapName}.");
        }

        int instanceId = AllocateInstanceId(dataDirectory, serverPath, targetMapName);
        try
        {
            _fileSystem.CreateDirectory(targetFolder);
            EnsureSubfolders(dataDirectory, targetMapName, name);
            PresetResult seeded = SeedServerConfig(
                serverPath, dataDirectory, targetMapName, name, instanceId,
                sourcePreset: sourcePresetName, sourceMapName: sourceMapName);
            if (!seeded.Success)
            {
                TryDelete(targetFolder);
                return Failure(seeded.Message);
            }

            CopyFileIfExists(
                PresetPaths.ModOrderPath(dataDirectory, sourceMapName, sourcePresetName),
                PresetPaths.ModOrderPath(dataDirectory, targetMapName, name));
            CopyTypesConfig(
                dataDirectory, sourceMapName, sourcePresetName, targetMapName, name);

            CopyDirectoryIfExists(
                PresetPaths.TypeFilesFolder(dataDirectory, sourceMapName, sourcePresetName),
                PresetPaths.TypeFilesFolder(dataDirectory, targetMapName, name));

            var meta = new PresetMetaData
            {
                InstanceId = instanceId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            ConfigJson.Write(_fileSystem, PresetPaths.PresetMetaPath(dataDirectory, targetMapName, name), meta);
        }
        catch (Exception ex)
        {
            TryDelete(targetFolder);
            return Failure($"Failed to duplicate preset: {ex.Message}");
        }

        return Success($"Duplicated preset \"{sourcePresetName}\" to \"{name}\" on {targetMapName} (instanceId {instanceId}).");
    }

    public PresetResult RenamePreset(string dataDirectory, string mapName, string presetName, string newName)
    {
        if (PresetPaths.IsDefaultPreset(presetName))
        {
            return Failure("The default preset cannot be renamed.");
        }

        string? name = NormalizePresetName(newName);
        if (name is null)
        {
            return Failure($"Invalid preset name. {PresetNameRequirement}");
        }

        if (PresetPaths.IsDefaultPreset(name))
        {
            return Failure("\"__default_preset__\" is reserved.");
        }

        string source = PresetPaths.PresetFolder(dataDirectory, mapName, presetName);
        if (!_fileSystem.DirectoryExists(source))
        {
            return Failure($"Preset \"{presetName}\" was not found.");
        }

        string target = PresetPaths.PresetFolder(dataDirectory, mapName, name);
        if (_fileSystem.DirectoryExists(target))
        {
            return Failure($"A preset named \"{name}\" already exists for {mapName}.");
        }

        try
        {
            _fileSystem.MoveDirectory(source, target);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to rename preset: {ex.Message}");
        }

        return Success($"Renamed preset \"{presetName}\" to \"{name}\".");
    }

    public PresetResult DeletePreset(string dataDirectory, string mapName, string presetName)
    {
        if (PresetPaths.IsDefaultPreset(presetName))
        {
            return Failure("The default preset cannot be deleted.");
        }

        string target = PresetPaths.PresetFolder(dataDirectory, mapName, presetName);
        if (!_fileSystem.DirectoryExists(target))
        {
            return Failure($"Preset \"{presetName}\" was not found.");
        }

        try
        {
            _fileSystem.DeleteDirectory(target, recursive: true);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to delete preset \"{presetName}\": {ex.Message}");
        }

        return Success($"Deleted preset \"{presetName}\".");
    }

    private PresetResult CreatePresetCore(
        string serverPath, string dataDirectory, string mapName, string presetName)
    {
        // The default preset deliberately adopts the server root's existing
        // instance ID when one is configured, so a world the user already played
        // (storage_<rootId>) stays attached to the default preset instead of
        // being orphaned. Named presets always get a fresh, collision-free ID.
        int? preferred = PresetPaths.IsDefaultPreset(presetName)
            ? _serverConfig.TryReadInstanceId(Path.Combine(serverPath, ConfigFileNames.ServerConfig))
            : null;
        int instanceId = ReserveInstanceId(dataDirectory, serverPath, mapName, preferred);
        string presetFolder = PresetPaths.PresetFolder(dataDirectory, mapName, presetName);

        try
        {
            _fileSystem.CreateDirectory(presetFolder);
            EnsureSubfolders(dataDirectory, mapName, presetName);

            PresetResult seeded = SeedServerConfig(serverPath, dataDirectory, mapName, presetName, instanceId);
            if (!seeded.Success)
            {
                TryDelete(presetFolder);
                return Failure(seeded.Message);
            }

            var meta = new PresetMetaData
            {
                InstanceId = instanceId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            ConfigJson.Write(_fileSystem, PresetPaths.PresetMetaPath(dataDirectory, mapName, presetName), meta);
        }
        catch (Exception ex)
        {
            TryDelete(presetFolder);
            return Failure($"Failed to create preset: {ex.Message}");
        }

        return Success($"Created preset \"{presetName}\" (instanceId {instanceId}).");
    }

    private void EnsureSubfolders(string dataDirectory, string mapName, string presetName)
    {
        _fileSystem.CreateDirectory(PresetPaths.TypeFilesFolder(dataDirectory, mapName, presetName));
        _fileSystem.CreateDirectory(PresetPaths.ProfilesFolder(dataDirectory, mapName, presetName));
        _fileSystem.CreateDirectory(PresetPaths.SavesFolder(dataDirectory, mapName, presetName));
    }

    /// <summary>
    /// Seeds the preset's <c>serverDZ.cfg</c>: copied from
    /// <paramref name="sourcePreset"/> (read from <paramref name="sourceMapName"/>,
    /// or the target map when null) when given, otherwise from the default preset
    /// (or the server root for the default preset itself), then rewritten so its
    /// <c>template</c> is the target map and its <c>instanceId</c> is the preset's
    /// own. Returns a failure when no real source config is available; a
    /// configuration is never fabricated.
    /// </summary>
    private PresetResult SeedServerConfig(
        string serverPath, string dataDirectory, string mapName, string presetName, int instanceId,
        string? sourcePreset = null, string? sourceMapName = null)
    {
        string target = PresetPaths.ServerConfigPath(dataDirectory, mapName, presetName);

        string? source = null;
        if (!string.IsNullOrWhiteSpace(sourcePreset))
        {
            string sourceMap = string.IsNullOrWhiteSpace(sourceMapName) ? mapName : sourceMapName;
            string sourceConfig = PresetPaths.ServerConfigPath(dataDirectory, sourceMap, sourcePreset);
            if (_fileSystem.FileExists(sourceConfig))
            {
                source = sourceConfig;
            }
        }

        if (source is null && !PresetPaths.IsDefaultPreset(presetName))
        {
            string defaultConfig = PresetPaths.ServerConfigPath(
                dataDirectory, mapName, PresetPaths.DefaultPresetName);
            if (_fileSystem.FileExists(defaultConfig))
            {
                source = defaultConfig;
            }
        }

        if (source is null)
        {
            string rootConfig = Path.Combine(serverPath, ConfigFileNames.ServerConfig);
            if (!string.IsNullOrWhiteSpace(serverPath) && _fileSystem.FileExists(rootConfig))
            {
                source = rootConfig;
            }
        }

        if (source is null)
        {
            // Never fabricate a configuration: a preset must be seeded from a real
            // server config, otherwise the server would start with a stub that
            // silently drops the user's settings.
            return Failure(
                $"serverDZ.cfg was not found in the server directory ({serverPath}). " +
                "Restore it (for example, verify the integrity of the DayZ Server files in Steam), " +
                "or copy a valid serverDZ.cfg into the server directory.");
        }

        _fileSystem.CopyFile(source, target);

        // The source must define the mission template; without it the preset cannot
        // be pointed at its map, so fail rather than overwrite the config.
        if (!_serverConfig.UpdateTemplate(target, mapName))
        {
            return Failure(
                $"The server configuration \"{source}\" has no template line and cannot be used to create a preset.");
        }

        _serverConfig.WriteInstanceId(target, instanceId);
        return Success($"Seeded serverDZ.cfg from \"{source}\".");
    }

    private void CopyFileIfExists(string source, string destination)
    {
        if (_fileSystem.FileExists(source))
        {
            _fileSystem.CopyFile(source, destination);
        }
    }

    private void CopyDirectoryIfExists(string source, string destination)
    {
        if (_fileSystem.DirectoryExists(source))
        {
            _fileSystem.CopyDirectory(source, destination);
        }
    }

    /// <summary>
    /// Copies a preset's <c>types_config.json</c> to the duplicated preset. Types
    /// configuration is scoped to a single map, so when the copy targets a different
    /// map the file is rewritten: its map key and <c>CurrentMap</c> become the
    /// destination map, otherwise the destination preset's types would be ignored.
    /// A same-map copy is a straight file copy.
    /// </summary>
    private void CopyTypesConfig(
        string dataDirectory, string sourceMapName, string sourcePresetName,
        string targetMapName, string targetPresetName)
    {
        string sourcePath = PresetPaths.TypesConfigPath(dataDirectory, sourceMapName, sourcePresetName);
        if (!_fileSystem.FileExists(sourcePath))
        {
            return;
        }

        string targetPath = PresetPaths.TypesConfigPath(dataDirectory, targetMapName, targetPresetName);
        if (string.Equals(sourceMapName, targetMapName, StringComparison.OrdinalIgnoreCase))
        {
            _fileSystem.CopyFile(sourcePath, targetPath);
            return;
        }

        ConfigLoadResult<TypesConfig> loaded = ConfigJson.Read<TypesConfig>(_fileSystem, sourcePath);
        var rewritten = new TypesConfig { CurrentMap = targetMapName };
        if (loaded.Status == ConfigLoadStatus.Success
            && loaded.Value is not null
            && loaded.Value.Maps.TryGetValue(sourceMapName, out MapTypesConfig? map))
        {
            rewritten.Maps[targetMapName] = map;
        }

        ConfigJson.Write(_fileSystem, targetPath, rewritten);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (_fileSystem.DirectoryExists(path))
            {
                _fileSystem.DeleteDirectory(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort: a failed cleanup leaves a partial preset that the user
            // can delete manually; the caller has already reported the failure.
        }
    }

    /// <summary>True when <paramref name="name"/> is a valid preset name (see <see cref="PresetNameRequirement"/>).</summary>
    public static bool IsValidPresetName(string? name) => NormalizePresetName(name) is not null;

    private static string? NormalizePresetName(string? presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return null;
        }

        string trimmed = presetName.Trim();
        if (trimmed.Length == 0
            || !trimmed.All(IsAllowedPresetNameChar)
            || trimmed.EndsWith('.')
            || ReservedDeviceNames.Contains(trimmed.Split('.')[0]))
        {
            return null;
        }

        return trimmed;
    }

    private static bool IsAllowedPresetNameChar(char c) =>
        (c >= 'A' && c <= 'Z')
        || (c >= 'a' && c <= 'z')
        || (c >= '0' && c <= '9')
        || c is '.' or '_' or '-' or '+';

    private static PresetResult Success(string message) => new() { Success = true, Message = message };

    private static PresetResult Failure(string message) => new() { Success = false, Message = message };
}
