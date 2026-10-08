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
/// <c>serverDZ.cfg</c>, mod order, types configuration, ModTypes, profiles and
/// world saves. <c>__default_preset__</c> is a normal preset with a reserved name
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
    /// Reads a preset's dedicated instance ID from its <c>preset-meta.json</c>,
    /// defaulting to 1 when the metadata is missing or unreadable.
    /// </summary>
    int ReadInstanceId(string dataDirectory, string mapName, string presetName);

    /// <summary>
    /// Allocates the next instance ID as <c>max(existing) + 1</c> across every
    /// preset of every map, or 1 when none exist.
    /// </summary>
    int AllocateInstanceId(string dataDirectory);

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
        return loaded.Status == ConfigLoadStatus.Success && loaded.Value is { InstanceId: > 0 }
            ? loaded.Value.InstanceId
            : 1;
    }

    public int AllocateInstanceId(string dataDirectory)
    {
        int max = 0;
        string root = PresetPaths.PresetsRoot(dataDirectory);
        if (!_fileSystem.DirectoryExists(root))
        {
            return 1;
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
                string metaPath = Path.Combine(mapRoot, preset, ConfigFileNames.PresetMeta);
                ConfigLoadResult<PresetMetaData> meta = ConfigJson.Read<PresetMetaData>(_fileSystem, metaPath);
                if (meta.Status == ConfigLoadStatus.Success && meta.Value is { InstanceId: > 0 })
                {
                    max = Math.Max(max, meta.Value.InstanceId);
                }
            }
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
        int instanceId = AllocateInstanceId(dataDirectory);
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
        _fileSystem.CreateDirectory(PresetPaths.ModTypesFolder(dataDirectory, mapName, presetName));
        _fileSystem.CreateDirectory(PresetPaths.ProfilesFolder(dataDirectory, mapName, presetName));
        _fileSystem.CreateDirectory(PresetPaths.SavesFolder(dataDirectory, mapName, presetName));
    }

    /// <summary>
    /// Seeds the preset's <c>serverDZ.cfg</c>: copied from the default preset (or
    /// the server root for the default preset itself), then rewritten so its
    /// <c>template</c> is the map and its <c>instanceId</c> is the preset's own.
    /// Falls back to a minimal valid config when no source file exists.
    /// </summary>
    private void SeedServerConfig(
        string serverPath, string dataDirectory, string mapName, string presetName, int instanceId)
    {
        string target = PresetPaths.ServerConfigPath(dataDirectory, mapName, presetName);

        string? source = null;
        if (!PresetPaths.IsDefaultPreset(presetName))
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
