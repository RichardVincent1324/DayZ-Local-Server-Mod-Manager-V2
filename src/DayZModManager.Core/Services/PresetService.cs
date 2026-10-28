using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a preset operation.</summary>
public sealed record PresetResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Non-fatal notices produced by the operation, for example when the
    /// server-root instance ID could not be adopted and a fresh one was used.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

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
    /// Reads a preset's dedicated instance ID. The preset's own
    /// <c>serverDZ.cfg</c> is authoritative because that is the value DayZ uses
    /// to locate <c>storage_&lt;id&gt;</c>. The metadata is used only when the
    /// config has no usable ID. Returns 0 when neither can supply a valid ID -
    /// never a made-up value that could collide with an existing world's storage
    /// slot.
    /// </summary>
    int ReadInstanceId(string dataDirectory, string mapName, string presetName);

    /// <summary>
    /// Returns human-readable problems with preset instance IDs: a metadata value
    /// that disagrees with <c>serverDZ.cfg</c>, or the same effective ID used by
    /// more than one preset. Empty when every preset's IDs are consistent and
    /// unique.
    /// </summary>
    IReadOnlyList<string> FindInstanceIdIssues(string dataDirectory);

    /// <summary>
    /// Returns only the blocking instance-ID problems: the same effective ID used
    /// by more than one preset (their worlds and <c>DZMMV2_ModList</c> junctions would be
    /// shared). Safe to use in a launch preflight.
    /// </summary>
    IReadOnlyList<string> FindInstanceIdConflicts(string dataDirectory);

    /// <summary>
    /// Allocates the next free instance ID for <paramref name="mapName"/> as
    /// <c>max(reserved) + 1</c>. Reserved IDs are every preset's ID across every
    /// map (IDs key the shared <c>DZMMV2_ModList</c> junction folder, so they must be
    /// globally unique) plus every live <c>storage_&lt;id&gt;</c> folder already
    /// present in the map's mission folder. Reserving the live storage IDs ensures
    /// a new preset is never bound to a world this tool did not create.
    /// </summary>
    int AllocateInstanceId(string dataDirectory, string serverPath, string mapName);

    /// <summary>
    /// Creates <c>__default_preset__</c> for a map when it does not yet exist,
    /// seeding its <c>serverDZ.cfg</c> from the server root. The root config's
    /// instance ID is adopted when no preset on any map already uses it, so an
    /// existing <c>storage_&lt;id&gt;</c> world stays attached; otherwise a fresh ID
    /// is allocated and reported as a warning. A default preset whose folder exists
    /// but is missing its <c>serverDZ.cfg</c> is reported as a failure instead of
    /// being re-seeded. Idempotent.
    /// </summary>
    PresetResult EnsureDefaultPreset(string serverPath, string dataDirectory, string mapName);

    /// <summary>
    /// Creates a new named preset for a map with a fresh, dedicated instance ID and
    /// an empty profiles folder. Its <c>serverDZ.cfg</c> is copied from that map's
    /// <c>__default_preset__</c>; when that config is missing the operation fails
    /// (there is no server-root fallback). Presets never share profile data.
    /// </summary>
    PresetResult CreatePreset(
        string serverPath, string dataDirectory, string mapName, string presetName);

    /// <summary>
    /// Creates a new preset as a copy of an existing preset's environment: its
    /// server configuration (with a fresh, dedicated instance ID), mod order, types
    /// configuration and generated type files. The copy may target the same map as
    /// the source or a different one; only the <c>serverDZ.cfg</c> template is
    /// rewritten for the destination map (the types configuration is map-agnostic
    /// and is copied as-is). Profiles and saves are never copied.
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

        IReadOnlyList<string> warnings = Array.Empty<string>();
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

                warnings = healed.Warnings;
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
            JunctionKey = Dzmmv2ModListFolder.PresetKey(ReadInstanceId(lookupDirectory, mapName, activePreset)),
        };

        return EnvironmentResolution.Resolved(environment, warnings);
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
        // The value in serverDZ.cfg is what DayZ actually uses to locate
        // storage_<id>, so it is authoritative. The metadata is the app-managed
        // record and is used only when the config has no usable ID.
        string configPath = PresetPaths.ServerConfigPath(dataDirectory, mapName, presetName);
        int? fromConfig = _serverConfig.TryReadInstanceId(configPath);
        if (fromConfig is > 0)
        {
            return fromConfig.Value;
        }

        int fromMeta = TryReadMetaInstanceId(dataDirectory, mapName, presetName);
        if (fromMeta > 0)
        {
            return fromMeta;
        }

        // Neither metadata nor config can identify the preset: report 0 rather
        // than inventing an ID. 0 is not a valid storage slot, so no pre-existing
        // world can be mistaken for this preset's.
        return 0;
    }

    public IReadOnlyList<string> FindInstanceIdIssues(string dataDirectory) =>
        FindInstanceIdIssues(dataDirectory, includeMismatches: true);

    public IReadOnlyList<string> FindInstanceIdConflicts(string dataDirectory) =>
        FindInstanceIdIssues(dataDirectory, includeMismatches: false);

    private IReadOnlyList<string> FindInstanceIdIssues(string dataDirectory, bool includeMismatches)
    {
        var issues = new List<string>();
        var owners = new Dictionary<int, string>();

        try
        {
            string root = PresetPaths.PresetsRoot(dataDirectory);
            if (!_fileSystem.DirectoryExists(root))
            {
                return issues;
            }

            foreach (string map in _fileSystem.GetDirectories(root))
            {
                string mapRoot = Path.Combine(root, map);
                if (!_fileSystem.DirectoryExists(mapRoot))
                {
                    continue;
                }

                foreach (string preset in _fileSystem.GetDirectories(mapRoot))
                {
                    string label = $"preset \"{preset}\" on {map}";
                    int metaId = TryReadMetaInstanceId(dataDirectory, map, preset);
                    int? configId = _serverConfig.TryReadInstanceId(
                        PresetPaths.ServerConfigPath(dataDirectory, map, preset));

                    if (includeMismatches && configId is > 0 && metaId > 0 && configId.Value != metaId)
                    {
                        issues.Add(
                            $"{label} has instanceId {metaId} in preset-meta.json but {configId} in serverDZ.cfg. " +
                            $"The server uses {configId}; align the preset metadata with its config.");
                    }

                    int effectiveId = configId is > 0 ? configId.Value : metaId;
                    if (effectiveId <= 0)
                    {
                        continue;
                    }

                    if (owners.TryGetValue(effectiveId, out string? existing))
                    {
                        issues.Add(
                            $"Instance ID {effectiveId} is used by both {existing} and {label}. " +
                            "Their worlds and DZMMV2_ModList junctions may be shared; make the IDs unique.");
                    }
                    else
                    {
                        owners[effectiveId] = label;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            issues.Add($"Could not inspect preset instance IDs: {ex.Message}");
        }

        return issues;
    }

    private int TryReadMetaInstanceId(string dataDirectory, string mapName, string presetName)
    {
        try
        {
            ConfigLoadResult<PresetMetaData> loaded = ConfigJson.Read<PresetMetaData>(
                _fileSystem, PresetPaths.PresetMetaPath(dataDirectory, mapName, presetName));
            return loaded.Status == ConfigLoadStatus.Success && loaded.Value is { InstanceId: > 0 }
                ? loaded.Value.InstanceId
                : 0;
        }
        catch (Exception)
        {
            // A locked/unreadable metadata file must not break startup or a launch
            // preflight; the config value (or 0) is used instead.
            return 0;
        }
    }

    public int AllocateInstanceId(string dataDirectory, string serverPath, string mapName) =>
        AllocateFreshInstanceId(dataDirectory, serverPath, mapName);

    /// <summary>
    /// Returns the instance ID for a newly created default preset. The server
    /// root's ID is adopted only when it is valid and no preset (on any map) uses
    /// it; that is what keeps an existing <c>storage_&lt;id&gt;</c> world attached.
    /// Otherwise a fresh ID is allocated and a warning explains why.
    /// </summary>
    private (int InstanceId, string? Warning) ResolveDefaultInstanceId(
        string serverPath, string dataDirectory, string mapName)
    {
        string rootConfig = Path.Combine(serverPath, ConfigFileNames.ServerConfig);
        int? rootId = _serverConfig.TryReadInstanceId(rootConfig);
        if (rootId is not > 0)
        {
            return (AllocateFreshInstanceId(dataDirectory, serverPath, mapName), null);
        }

        if (TryDescribePresetUsingId(dataDirectory, rootId.Value, out string owner))
        {
            return (AllocateFreshInstanceId(dataDirectory, serverPath, mapName),
                $"The server-root instanceId {rootId} is already used by {owner}. " +
                $"The default preset for {mapName} will use a fresh instanceId and will not attach to that world.");
        }

        return (rootId.Value, null);
    }

    /// <summary>
    /// Allocates a fresh ID that is not used by any preset on any map and does not
    /// match a live <c>storage_&lt;id&gt;</c> folder on the target map.
    /// </summary>
    private int AllocateFreshInstanceId(string dataDirectory, string serverPath, string mapName)
    {
        var presetIds = new HashSet<int>(CollectPresetIds(dataDirectory).Keys);

        // Live storage folders on this map are reserved so a fresh preset is never
        // bound to a world this tool did not create.
        var storageIds = new HashSet<int>(
            StorageFolders.ListInstanceIds(_fileSystem, serverPath, mapName).Where(id => id > 0));

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

    /// <summary>
    /// Returns every instance ID currently owned by a preset, keyed by ID, with a
    /// human-readable description of the owner (preset and map).
    /// </summary>
    private IReadOnlyDictionary<int, string> CollectPresetIds(string dataDirectory)
    {
        var ids = new Dictionary<int, string>();
        string root = PresetPaths.PresetsRoot(dataDirectory);
        if (!_fileSystem.DirectoryExists(root))
        {
            return ids;
        }

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
                    ids.TryAdd(id, $"preset \"{preset}\" on {map}");
                }
            }
        }

        return ids;
    }

    private bool TryDescribePresetUsingId(string dataDirectory, int instanceId, out string owner)
    {
        if (CollectPresetIds(dataDirectory).TryGetValue(instanceId, out string? description))
        {
            owner = description;
            return true;
        }

        owner = string.Empty;
        return false;
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

        return CreateDefaultPresetCore(serverPath, dataDirectory, mapName);
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
        string? warning = null;
        if (instanceId <= 0)
        {
            (instanceId, warning) = ResolveDefaultInstanceId(serverPath, dataDirectory, mapName);
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

        return Success($"Repaired the default preset metadata for {mapName} (instanceId {instanceId}).", warning);
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

        return CreateNamedPresetCore(serverPath, dataDirectory, mapName, name);
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

        string sourceConfig = PresetPaths.ServerConfigPath(dataDirectory, sourceMapName, sourcePresetName);
        if (!_fileSystem.FileExists(sourceConfig))
        {
            return Failure(
                $"Preset \"{sourcePresetName}\" has no serverDZ.cfg ({sourceConfig}); it cannot be duplicated.");
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
                sourceConfig,
                PresetPaths.ServerConfigPath(dataDirectory, targetMapName, name),
                targetMapName,
                instanceId);
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

    private PresetResult CreateDefaultPresetCore(
        string serverPath, string dataDirectory, string mapName)
    {
        (int instanceId, string? warning) = ResolveDefaultInstanceId(serverPath, dataDirectory, mapName);
        string rootConfig = Path.Combine(serverPath, ConfigFileNames.ServerConfig);
        return CreatePresetAt(
            dataDirectory, mapName, PresetPaths.DefaultPresetName, instanceId, rootConfig, warning);
    }

    private PresetResult CreateNamedPresetCore(
        string serverPath, string dataDirectory, string mapName, string presetName)
    {
        string defaultConfig = PresetPaths.ServerConfigPath(
            dataDirectory, mapName, PresetPaths.DefaultPresetName);
        if (!_fileSystem.FileExists(defaultConfig))
        {
            return Failure(
                $"The default preset for {mapName} has no serverDZ.cfg ({defaultConfig}); " +
                "restore it before adding a preset.");
        }

        int instanceId = AllocateInstanceId(dataDirectory, serverPath, mapName);
        return CreatePresetAt(dataDirectory, mapName, presetName, instanceId, defaultConfig, warning: null);
    }

    /// <summary>
    /// Creates a preset folder and seeds its config from an explicit source. Never
    /// discovers a fallback source: the caller decides whether the source is the
    /// server root (default preset), the map's default preset (named preset) or a
    /// source preset (duplicate).
    /// </summary>
    private PresetResult CreatePresetAt(
        string dataDirectory, string mapName, string presetName, int instanceId,
        string sourceConfig, string? warning)
    {
        string presetFolder = PresetPaths.PresetFolder(dataDirectory, mapName, presetName);

        try
        {
            _fileSystem.CreateDirectory(presetFolder);
            EnsureSubfolders(dataDirectory, mapName, presetName);

            PresetResult seeded = SeedServerConfig(
                sourceConfig,
                PresetPaths.ServerConfigPath(dataDirectory, mapName, presetName),
                mapName,
                instanceId);
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

        return Success($"Created preset \"{presetName}\" (instanceId {instanceId}).", warning);
    }

    private void EnsureSubfolders(string dataDirectory, string mapName, string presetName)
    {
        _fileSystem.CreateDirectory(PresetPaths.TypeFilesFolder(dataDirectory, mapName, presetName));
        _fileSystem.CreateDirectory(PresetPaths.ProfilesFolder(dataDirectory, mapName, presetName));
        _fileSystem.CreateDirectory(PresetPaths.SavesFolder(dataDirectory, mapName, presetName));
    }

    /// <summary>
    /// Copies <paramref name="sourceConfig"/> and rewrites its <c>template</c> and
    /// <c>instanceId</c> for the new preset. Never fabricates or falls back to
    /// another config; a missing source is a failure.
    /// </summary>
    private PresetResult SeedServerConfig(
        string sourceConfig, string targetConfig, string mapName, int instanceId)
    {
        if (!_fileSystem.FileExists(sourceConfig))
        {
            return Failure(
                $"serverDZ.cfg was not found at \"{sourceConfig}\". " +
                "Restore it (for example, verify the integrity of the DayZ Server files in Steam), " +
                "or copy a valid serverDZ.cfg there.");
        }

        _fileSystem.CopyFile(sourceConfig, targetConfig);

        // The source must define the mission template; without it the preset cannot
        // be pointed at its map, so fail rather than leave a broken config.
        if (!_serverConfig.UpdateTemplate(targetConfig, mapName))
        {
            return Failure(
                $"The server configuration \"{sourceConfig}\" has no template line and cannot be used to create a preset.");
        }

        _serverConfig.WriteInstanceId(targetConfig, instanceId);
        return Success($"Seeded serverDZ.cfg from \"{sourceConfig}\".");
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
    /// Copies a preset's <c>types_config.json</c> to the duplicated preset. The
    /// file is map-agnostic (the preset folder determines the map), so a straight
    /// copy is correct even when the copy targets a different map.
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

        _fileSystem.CopyFile(
            sourcePath,
            PresetPaths.TypesConfigPath(dataDirectory, targetMapName, targetPresetName));
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

    private static PresetResult Success(string message, string? warning = null) =>
        new()
        {
            Success = true,
            Message = message,
            Warnings = warning is null ? Array.Empty<string>() : new[] { warning },
        };

    private static PresetResult Failure(string message) => new() { Success = false, Message = message };
}
