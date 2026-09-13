using System.Text.Json;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Loads and saves <see cref="Models.Settings"/>.</summary>
public interface ISettingsService
{
    ConfigLoadResult<Settings> Load(string dataDirectory);

    void Save(string dataDirectory, Settings settings);

    /// <summary>
    /// Preserves a corrupt settings file as "settings.json.corrupt" so it is not
    /// silently lost when defaults are written. Returns true when the corrupt
    /// contents are preserved (or an earlier backup already exists).
    /// </summary>
    bool BackupCorrupt(string dataDirectory);
}

public sealed class SettingsService : ISettingsService
{
    private readonly IFileSystem _fileSystem;

    public SettingsService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ConfigLoadResult<Settings> Load(string dataDirectory)
    {
        string path = Path.Combine(dataDirectory, ConfigFileNames.Settings);
        if (!_fileSystem.FileExists(path))
        {
            return ConfigLoadResult<Settings>.Missing();
        }

        try
        {
            string json = _fileSystem.ReadAllText(path);

            Settings? settings = JsonSerializer.Deserialize<Settings>(json, ConfigJson.Options);
            return settings is null
                ? ConfigLoadResult<Settings>.Corrupt()
                : ConfigLoadResult<Settings>.Success(settings);
        }
        catch (JsonException)
        {
            return ConfigLoadResult<Settings>.Corrupt();
        }
    }

    public void Save(string dataDirectory, Settings settings)
    {
        string path = Path.Combine(dataDirectory, ConfigFileNames.Settings);
        ConfigJson.Write(_fileSystem, path, settings);
    }

    public bool BackupCorrupt(string dataDirectory)
    {
        string sourcePath = Path.Combine(dataDirectory, ConfigFileNames.Settings);
        string backupPath = sourcePath + ".corrupt";

        if (!_fileSystem.FileExists(sourcePath))
        {
            return false;
        }

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
