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
        // A preset is scoped to a single map, so persist only that map's
        // configuration. Writing the whole in-memory dictionary would copy every
        // other map's settings into this preset, letting them leak between
        // presets and be dropped when a preset is reloaded.
        var scoped = new TypesConfig { CurrentMap = config.CurrentMap };
        if (!string.IsNullOrWhiteSpace(config.CurrentMap)
            && config.Maps.TryGetValue(config.CurrentMap, out MapTypesConfig? map))
        {
            scoped.Maps[config.CurrentMap] = map;
        }

        string path = Path.Combine(presetFolder, ConfigFileNames.TypesConfig);
        ConfigJson.Write(_fileSystem, path, scoped);
    }
}
