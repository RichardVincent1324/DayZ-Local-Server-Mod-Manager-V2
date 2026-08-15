using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Loads and saves the ordered list of loaded mods (mod_order.json).</summary>
public interface IModOrderStore
{
    ConfigLoadResult<IReadOnlyList<string>> Load(string dataDirectory);

    void Save(string dataDirectory, IReadOnlyList<string> mods);
}

public sealed class ModOrderStore : IModOrderStore
{
    private readonly IFileSystem _fileSystem;

    public ModOrderStore(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ConfigLoadResult<IReadOnlyList<string>> Load(string dataDirectory)
    {
        string path = Path.Combine(dataDirectory, ConfigFileNames.ModOrder);
        ConfigLoadResult<List<string>> result = ConfigJson.Read<List<string>>(_fileSystem, path);

        return result.Status switch
        {
            ConfigLoadStatus.Success => ConfigLoadResult<IReadOnlyList<string>>.Success(result.Value!),
            ConfigLoadStatus.Missing => ConfigLoadResult<IReadOnlyList<string>>.Missing(),
            _ => ConfigLoadResult<IReadOnlyList<string>>.Corrupt(),
        };
    }

    public void Save(string dataDirectory, IReadOnlyList<string> mods)
    {
        string path = Path.Combine(dataDirectory, ConfigFileNames.ModOrder);
        ConfigJson.Write(_fileSystem, path, mods.ToList());
    }
}
