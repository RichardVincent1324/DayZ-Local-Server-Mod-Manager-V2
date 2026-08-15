using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Loads and saves the per-map types configuration (types_config.json).</summary>
public interface ITypesConfigStore
{
    ConfigLoadResult<TypesConfig> Load(string dataDirectory);

    void Save(string dataDirectory, TypesConfig config);
}

public sealed class TypesConfigStore : ITypesConfigStore
{
    private readonly IFileSystem _fileSystem;

    public TypesConfigStore(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ConfigLoadResult<TypesConfig> Load(string dataDirectory)
    {
        string path = Path.Combine(dataDirectory, ConfigFileNames.TypesConfig);
        ConfigLoadResult<TypesConfig> result = ConfigJson.Read<TypesConfig>(_fileSystem, path);

        return result.Status switch
        {
            ConfigLoadStatus.Success => ConfigLoadResult<TypesConfig>.Success(result.Value!),
            ConfigLoadStatus.Missing => ConfigLoadResult<TypesConfig>.Missing(),
            _ => ConfigLoadResult<TypesConfig>.Corrupt(),
        };
    }

    public void Save(string dataDirectory, TypesConfig config)
    {
        string path = Path.Combine(dataDirectory, ConfigFileNames.TypesConfig);
        ConfigJson.Write(_fileSystem, path, config);
    }
}
