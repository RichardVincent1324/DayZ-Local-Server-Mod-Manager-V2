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

    /// <summary>True when the preset folder and its metadata exist.</summary>
    bool PresetExists(string dataDirectory, string mapName, string presetName);

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
    /// seeding its <c>serverDZ.cfg</c> from the server root. Idempotent.
    /// </summary>
    PresetResult EnsureDefaultPreset(string serverPath, string dataDirectory, string mapName);

    /// <summary>
    /// Creates a new named preset for a map with a fresh, dedicated instance ID.
    /// When <paramref name="copyProfilesFromDefault"/> is set, the default preset's
    /// profile data is copied; otherwise the preset gets an empty profiles folder.
    /// </summary>
    PresetResult CreatePreset(
        string serverPath, string dataDirectory, string mapName, string presetName, bool copyProfilesFromDefault);

    /// <summary>
    /// Creates a new preset as a copy of an existing preset's environment: its
    /// server configuration (with a fresh, dedicated instance ID), mod order, types
    /// configuration and generated type files. Profiles are copied only when
    /// <paramref name="copyProfiles"/> is set. Saves are never copied.
    /// </summary>
    PresetResult DuplicatePreset(
        string serverPath, string dataDirectory, string mapName,
        string sourcePresetName, string newPresetName, bool copyProfiles);

    /// <summary>Renames a user preset. The reserved default preset cannot be renamed.</summary>
    PresetResult RenamePreset(string dataDirectory, string mapName, string presetName, string newName);

    /// <summary>Deletes a user preset. The reserved default preset cannot be deleted.</summary>
    PresetResult DeletePreset(string dataDirectory, string mapName, string presetName);
}

public sealed class PresetService : IPresetService
{
    private const string MinimalServerConfigTemplate =
        "class Missions\n{{\n    class DayZ\n    {{\n        template=\"{0}\";\n    }};\n}};\n";

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
        _fileSystem.DirectoryExists(PresetPaths.PresetFolder(dataDirectory, mapName, presetName));

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
            return Success($"Default preset already exists for {mapName}.");
        }

        return CreatePresetCore(serverPath, dataDirectory, mapName, PresetPaths.DefaultPresetName, copyProfiles: false);
    }

    public PresetResult CreatePreset(
        string serverPath, string dataDirectory, string mapName, string presetName, bool copyProfilesFromDefault)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return Failure("A map is required to create a preset.");
        }

        string? name = NormalizePresetName(presetName);
        if (name is null)
        {
            return Failure("Preset name cannot be empty or contain invalid characters.");
        }

        if (PresetPaths.IsDefaultPreset(name))
        {
            return Failure("\"__default_preset__\" is reserved and is created automatically.");
        }

        if (_fileSystem.DirectoryExists(PresetPaths.PresetFolder(dataDirectory, mapName, name)))
        {
            return Failure($"A preset named \"{name}\" already exists for {mapName}.");
        }

        return CreatePresetCore(serverPath, dataDirectory, mapName, name, copyProfilesFromDefault);
    }

    public PresetResult DuplicatePreset(
        string serverPath, string dataDirectory, string mapName,
        string sourcePresetName, string newPresetName, bool copyProfiles)
    {
        string? name = NormalizePresetName(newPresetName);
        if (name is null)
        {
            return Failure("Preset name cannot be empty or contain invalid characters.");
        }

        if (PresetPaths.IsDefaultPreset(name))
        {
            return Failure("\"__default_preset__\" is reserved and is created automatically.");
        }

        string sourceFolder = PresetPaths.PresetFolder(dataDirectory, mapName, sourcePresetName);
        if (!_fileSystem.DirectoryExists(sourceFolder))
        {
            return Failure($"Preset \"{sourcePresetName}\" was not found.");
        }

        string targetFolder = PresetPaths.PresetFolder(dataDirectory, mapName, name);
        if (_fileSystem.DirectoryExists(targetFolder))
        {
            return Failure($"A preset named \"{name}\" already exists for {mapName}.");
        }

        int instanceId = AllocateInstanceId(dataDirectory, serverPath, mapName);
        try
        {
            _fileSystem.CreateDirectory(targetFolder);
            EnsureSubfolders(dataDirectory, mapName, name);
            SeedServerConfig(serverPath, dataDirectory, mapName, name, instanceId, sourcePresetName);

            CopyFileIfExists(
                PresetPaths.ModOrderPath(dataDirectory, mapName, sourcePresetName),
                PresetPaths.ModOrderPath(dataDirectory, mapName, name));
            CopyFileIfExists(
                PresetPaths.TypesConfigPath(dataDirectory, mapName, sourcePresetName),
                PresetPaths.TypesConfigPath(dataDirectory, mapName, name));

            CopyDirectoryIfExists(
                PresetPaths.TypeFilesFolder(dataDirectory, mapName, sourcePresetName),
                PresetPaths.TypeFilesFolder(dataDirectory, mapName, name));

            if (copyProfiles)
            {
                CopyDirectoryIfExists(
                    PresetPaths.ProfilesFolder(dataDirectory, mapName, sourcePresetName),
                    PresetPaths.ProfilesFolder(dataDirectory, mapName, name));
            }

            var meta = new PresetMetaData
            {
                InstanceId = instanceId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            ConfigJson.Write(_fileSystem, PresetPaths.PresetMetaPath(dataDirectory, mapName, name), meta);
        }
        catch (Exception ex)
        {
            TryDelete(targetFolder);
            return Failure($"Failed to duplicate preset: {ex.Message}");
        }

        return Success($"Duplicated preset \"{sourcePresetName}\" to \"{name}\" (instanceId {instanceId}).");
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
            return Failure("Preset name cannot be empty or contain invalid characters.");
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
        string serverPath, string dataDirectory, string mapName, string presetName, bool copyProfiles)
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
            SeedServerConfig(serverPath, dataDirectory, mapName, presetName, instanceId);

            var meta = new PresetMetaData
            {
                InstanceId = instanceId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            ConfigJson.Write(_fileSystem, PresetPaths.PresetMetaPath(dataDirectory, mapName, presetName), meta);

            if (copyProfiles)
            {
                CopyProfilesFromDefault(dataDirectory, mapName, presetName);
            }
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
    /// <paramref name="sourcePreset"/> when given, otherwise from the default preset
    /// (or the server root for the default preset itself), then rewritten so its
    /// <c>template</c> is the map and its <c>instanceId</c> is the preset's own.
    /// Falls back to a minimal valid config when no source file exists.
    /// </summary>
    private void SeedServerConfig(
        string serverPath, string dataDirectory, string mapName, string presetName, int instanceId,
        string? sourcePreset = null)
    {
        string target = PresetPaths.ServerConfigPath(dataDirectory, mapName, presetName);

        string? source = null;
        if (!string.IsNullOrWhiteSpace(sourcePreset))
        {
            string sourceConfig = PresetPaths.ServerConfigPath(dataDirectory, mapName, sourcePreset);
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

        if (source is not null)
        {
            _fileSystem.CopyFile(source, target);
        }
        else
        {
            _fileSystem.WriteAllText(target, string.Format(MinimalServerConfigTemplate, mapName));
        }

        // A source config might lack one of these lines (or be a partial file);
        // make sure both are present and correct for this preset.
        if (!_serverConfig.UpdateTemplate(target, mapName))
        {
            _fileSystem.WriteAllText(target, string.Format(MinimalServerConfigTemplate, mapName));
        }

        _serverConfig.WriteInstanceId(target, instanceId);
    }

    private void CopyProfilesFromDefault(string dataDirectory, string mapName, string presetName)
    {
        string defaultProfiles = PresetPaths.ProfilesFolder(
            dataDirectory, mapName, PresetPaths.DefaultPresetName);
        if (!_fileSystem.DirectoryExists(defaultProfiles))
        {
            return;
        }

        string targetProfiles = PresetPaths.ProfilesFolder(dataDirectory, mapName, presetName);
        _fileSystem.CopyDirectory(defaultProfiles, targetProfiles);
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

    private static string? NormalizePresetName(string? presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return null;
        }

        string trimmed = presetName.Trim();
        if (trimmed.Length == 0
            || trimmed.Equals(".", StringComparison.Ordinal)
            || trimmed.Equals("..", StringComparison.Ordinal)
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.EndsWith('.')
            || ReservedDeviceNames.Contains(trimmed.Split('.')[0]))
        {
            return null;
        }

        return trimmed;
    }

    private static PresetResult Success(string message) => new() { Success = true, Message = message };

    private static PresetResult Failure(string message) => new() { Success = false, Message = message };
}
