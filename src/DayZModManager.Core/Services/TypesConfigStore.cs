using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>
/// Loads and saves a preset's types configuration
/// (<c>&lt;preset&gt;\types_config.json</c>). Types configuration is preset-level:
/// it belongs to the active preset, never to an individual save. Because a preset
/// is scoped to a single map, the file holds that map's configuration.
/// </summary>
public interface ITypesConfigStore
{
    ConfigLoadResult<TypesConfig> Load(string presetFolder);

    void Save(string presetFolder, TypesConfig config);
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
        ConfigLoadResult<TypesConfig> result = ConfigJson.Read<TypesConfig>(_fileSystem, path);

        return result.Status switch
        {
            ConfigLoadStatus.Success => ConfigLoadResult<TypesConfig>.Success(result.Value!),
            ConfigLoadStatus.Missing => ConfigLoadResult<TypesConfig>.Missing(),
            _ => ConfigLoadResult<TypesConfig>.Corrupt(),
        };
    }

    public void Save(string presetFolder, TypesConfig config)
    {
        string path = Path.Combine(presetFolder, ConfigFileNames.TypesConfig);
        ConfigJson.Write(_fileSystem, path, config);
    }
}
