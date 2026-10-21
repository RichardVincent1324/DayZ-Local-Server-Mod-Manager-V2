using System.Text.Json;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>
/// Loads and saves a preset's types configuration
/// (<c>&lt;preset&gt;\types_config.json</c>). Types configuration is preset-level:
/// it belongs to the active preset, never to an individual save. Because a preset
/// is scoped to a single map, the file contains only that map's mods; the map is
/// implied by the preset folder path.
/// </summary>
public interface ITypesConfigStore
{
    ConfigLoadResult<TypesConfig> Load(string presetFolder);

    void Save(string presetFolder, TypesConfig config);

    /// <summary>
    /// Preserves an unreadable or unsupported <c>types_config.json</c> as
    /// <c>types_config.json.corrupt</c> so the user's data is never silently lost.
    /// Returns true when the file is backed up (or a backup already exists).
    /// </summary>
    bool BackupCorrupt(string presetFolder);
}

public sealed class TypesConfigStore : ITypesConfigStore
{
    private readonly IFileSystem _fileSystem;

    public TypesConfigStore(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ConfigLoadResult<TypesConfig> Load(string presetFolder)
    {
        string path = Path.Combine(presetFolder, ConfigFileNames.TypesConfig);
        if (!_fileSystem.FileExists(path))
        {
            return ConfigLoadResult<TypesConfig>.Missing();
        }

        return TryReadConfig(path, out TypesConfig? config)
            ? ConfigLoadResult<TypesConfig>.Success(config!)
            : ConfigLoadResult<TypesConfig>.Corrupt();
    }

    public void Save(string presetFolder, TypesConfig config)
    {
        string path = Path.Combine(presetFolder, ConfigFileNames.TypesConfig);

        // Never overwrite a corrupt or old-format file without preserving it first.
        if (_fileSystem.FileExists(path)
            && !TryReadConfig(path, out _)
            && !BackupCorrupt(presetFolder))
        {
            throw new IOException(
                $"Refusing to overwrite corrupt or unsupported {ConfigFileNames.TypesConfig} because it could not be backed up: {path}");
        }

        ConfigJson.Write(_fileSystem, path, config);
        config.IsCorrupt = false;
    }

    public bool BackupCorrupt(string presetFolder)
    {
        string sourcePath = Path.Combine(presetFolder, ConfigFileNames.TypesConfig);
        if (!_fileSystem.FileExists(sourcePath) || TryReadConfig(sourcePath, out _))
        {
            return false;
        }

        string backupPath = sourcePath + ".corrupt";
        if (_fileSystem.FileExists(backupPath))
        {
            return true;
        }

        try
        {
            _fileSystem.CopyFile(sourcePath, backupPath);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the current single-map shape ({ "mods": [...] }). The old pre-release
    /// envelope ({ "maps": ..., "currentMap": ... }) is deliberately rejected so it
    /// is preserved by the corruption flow instead of being read as empty and
    /// silently overwritten.
    /// </summary>
    private bool TryReadConfig(string path, out TypesConfig? config)
    {
        config = null;

        try
        {
            string raw = _fileSystem.ReadAllText(path);
            using JsonDocument doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || doc.RootElement.TryGetProperty("maps", out _)
                || doc.RootElement.TryGetProperty("currentMap", out _))
            {
                return false;
            }

            config = JsonSerializer.Deserialize<TypesConfig>(raw, ConfigJson.Options);
            return config is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
