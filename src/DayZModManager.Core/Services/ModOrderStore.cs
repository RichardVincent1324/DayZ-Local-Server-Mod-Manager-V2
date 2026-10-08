using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>
/// Loads and saves a preset's ordered list of loaded mods
/// (<c>&lt;preset&gt;\mod_order.json</c>). The mod order is preset-level: it
/// belongs to the active preset, never to an individual save.
/// </summary>
public interface IModOrderStore
{
    ConfigLoadResult<IReadOnlyList<string>> Load(string presetFolder);

    void Save(string presetFolder, IReadOnlyList<string> mods);
}

public sealed class ModOrderStore : IModOrderStore
{
    private readonly IFileSystem _fileSystem;

    public ModOrderStore(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ConfigLoadResult<IReadOnlyList<string>> Load(string presetFolder)
    {
        string path = Path.Combine(presetFolder, ConfigFileNames.ModOrder);
        ConfigLoadResult<List<string>> result = ConfigJson.Read<List<string>>(_fileSystem, path);

        return result.Status switch
        {
            ConfigLoadStatus.Success => ConfigLoadResult<IReadOnlyList<string>>.Success(result.Value!),
            ConfigLoadStatus.Missing => ConfigLoadResult<IReadOnlyList<string>>.Missing(),
            _ => ConfigLoadResult<IReadOnlyList<string>>.Corrupt(),
        };
    }

    public void Save(string presetFolder, IReadOnlyList<string> mods)
    {
        string path = Path.Combine(presetFolder, ConfigFileNames.ModOrder);
        ConfigJson.Write(_fileSystem, path, mods.ToList());
    }
}
