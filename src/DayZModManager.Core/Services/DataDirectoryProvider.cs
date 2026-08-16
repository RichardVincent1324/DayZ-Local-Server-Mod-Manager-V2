using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>
/// Resolves the directory where configuration data files are persisted and
/// orchestrates relocation when that location changes.
/// </summary>
public interface IDataDirectoryProvider
{
    /// <summary>Current effective data directory.</summary>
    string Current { get; }

    /// <summary>Runs the startup bootstrap: rediscovers or pins the active data directory.</summary>
    void Initialize();

    /// <summary>
    /// Resolves the effective data directory for a settings snapshot, honoring an
    /// explicit override, a previously pinned location, or (for a fresh install)
    /// the server-path derived default.
    /// </summary>
    string Resolve(Settings settings);

    /// <summary>
    /// Moves existing data files to <paramref name="directory"/>, persists
    /// <paramref name="settings"/> there, updates the pointer anchor, and makes
    /// it the current data directory.
    /// </summary>
    void MoveTo(string directory, Settings settings);
}

public sealed class DataDirectoryProvider : IDataDirectoryProvider
{
    private static readonly string[] DataFileNames =
    {
        ConfigFileNames.Settings,
        ConfigFileNames.ModOrder,
        ConfigFileNames.TypesConfig,
    };

    private readonly IFileSystem _fileSystem;

    private string _current = string.Empty;
    private bool _pinned;

    public DataDirectoryProvider(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public string Current => _current;

    public void Initialize()
    {
        string legacy = AppPaths.LegacyDirectory();
        string pointerPath = PointerPath();

        if (_fileSystem.FileExists(pointerPath))
        {
            string? pointed = ReadPointer();
            _current = string.IsNullOrWhiteSpace(pointed) ? legacy : pointed;
            _pinned = true;
            return;
        }

        if (_fileSystem.FileExists(Path.Combine(legacy, ConfigFileNames.Settings)))
        {
            // Existing install: keep the legacy location until the user changes it.
            WritePointer(legacy);
            _current = legacy;
            _pinned = true;
            return;
        }

        // Fresh install: start at the bootstrap anchor. The server-path derived
        // default is adopted once a server path is configured.
        _current = legacy;
        _pinned = false;
    }

    public string Resolve(Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.DataDirectory))
        {
            return settings.DataDirectory;
        }

        if (_pinned)
        {
            return _current;
        }

        return AppPaths.Resolve(null, settings.ServerPath);
    }

    public void MoveTo(string directory, Settings settings)
    {
        string target = Normalize(directory);
        _fileSystem.CreateDirectory(target);

        if (!string.Equals(target, _current, StringComparison.OrdinalIgnoreCase))
        {
            MigrateDataFiles(_current, target);
        }

        _current = target;
        _pinned = true;
        WritePointer(target);

        ConfigJson.Write(_fileSystem, Path.Combine(target, ConfigFileNames.Settings), settings);
    }

    private void MigrateDataFiles(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (string fileName in DataFileNames)
        {
            try
            {
                string sourcePath = Path.Combine(source, fileName);
                if (!_fileSystem.FileExists(sourcePath))
                {
                    continue;
                }

                string targetPath = Path.Combine(target, fileName);
                if (!_fileSystem.FileExists(targetPath))
                {
                    _fileSystem.CopyFile(sourcePath, targetPath);
                }

                _fileSystem.DeleteFile(sourcePath);
            }
            catch (Exception)
            {
                // Best-effort migration: leave the file where it is rather than
                // failing the whole relocation.
            }
        }
    }

    private string? ReadPointer()
    {
        try
        {
            return _fileSystem.ReadAllText(PointerPath()).Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void WritePointer(string directory)
    {
        try
        {
            string legacy = AppPaths.LegacyDirectory();
            _fileSystem.CreateDirectory(legacy);
            _fileSystem.WriteAllText(PointerPath(), directory);
        }
        catch (Exception)
        {
            // Best effort: the pointer is only used to rediscover the data
            // directory on startup; failing to write it must not break startup
            // or an Apply.
        }
    }

    private static string PointerPath() => Path.Combine(AppPaths.LegacyDirectory(), AppPaths.PointerFileName);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(path.Trim());
}
