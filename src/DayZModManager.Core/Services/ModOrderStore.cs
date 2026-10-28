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

    /// <summary>
    /// Preserves an unreadable <c>mod_order.json</c> as
    /// <c>mod_order.json.corrupt</c> so the user's data is never silently lost.
    /// Returns true when the corrupt file is backed up (or a backup already exists).
    /// </summary>
    bool BackupCorrupt(string presetFolder);
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

        // Never overwrite a corrupt file without preserving it first.
        if (_fileSystem.FileExists(path)
            && ConfigJson.Read<List<string>>(_fileSystem, path).Status == ConfigLoadStatus.Corrupt
            && !BackupCorrupt(presetFolder))
        {
            throw new IOException(
                $"Refusing to overwrite corrupt {ConfigFileNames.ModOrder} because it could not be backed up: {path}");
        }

        ConfigJson.Write(_fileSystem, path, mods.ToList());
    }

    public bool BackupCorrupt(string presetFolder)
    {
        string sourcePath = Path.Combine(presetFolder, ConfigFileNames.ModOrder);
        if (!_fileSystem.FileExists(sourcePath)
            || ConfigJson.Read<List<string>>(_fileSystem, sourcePath).Status != ConfigLoadStatus.Corrupt)
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
}
