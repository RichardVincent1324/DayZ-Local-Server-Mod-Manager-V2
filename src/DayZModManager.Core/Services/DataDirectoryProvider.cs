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

        /// <summary>Runs the startup bootstrap: rediscovers the active data directory.</summary>
        void Initialize();

        /// <summary>
        /// Resolves the effective data directory for a settings snapshot: a
        /// per-server subfolder under the server path once configured, otherwise
        /// the legacy bootstrap directory.
        /// </summary>
        string Resolve(Settings settings);

        /// <summary>
        /// Moves existing data files to <paramref name="directory"/>, persists
        /// <paramref name="settings"/> there, updates the pointer anchor, and makes
        /// it the current data directory. Throws when a critical file (the types
        /// configuration or the progress saves library) cannot be migrated, in which
        /// case the current directory and the pointer are left on the source so user
        /// data is never stranded in a directory the app stops reading.
        /// </summary>
        void MoveTo(string directory, Settings settings);
    }

    public sealed class DataDirectoryProvider : IDataDirectoryProvider
    {
        // Only the types configuration must be carried across a relocation: the
        // ApplyService authors settings.json and mod_order.json into the target
        // directory before MoveTo runs (and MoveTo re-authors settings.json), so
        // migrating those two over the freshly-written copies would clobber the
        // just-applied mod order. Saves are migrated separately.
        private static readonly string[] DataFileNames =
        {
            ConfigFileNames.TypesConfig,
        };

        private readonly IFileSystem _fileSystem;

        private string _current = string.Empty;

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

                // Honor the pointer only while it points at a directory that still
                // exists. A stale pointer (e.g. the data folder was deleted) must not
                // pin the app to a location that would be silently recreated empty.
                if (!string.IsNullOrWhiteSpace(pointed)
                    && _fileSystem.DirectoryExists(pointed))
                {
                    _current = pointed;
                    return;
                }
            }

            // Bootstrap at the legacy anchor until a server path is configured; the
            // server-path derived default is adopted once an Apply relocates there.
            _current = legacy;
        }

        public string Resolve(Settings settings) =>
            AppPaths.Resolve(settings.ServerPath);

    public void MoveTo(string directory, Settings settings)
    {
        string target = Normalize(directory);
        _fileSystem.CreateDirectory(target);

        if (!string.Equals(target, _current, StringComparison.OrdinalIgnoreCase))
        {
            // A failed migration must never silently strand user data (the per-map
            // types configuration and the progress saves) in a directory the app
            // stops reading. Abort the relocation before the pointer moves so the
            // caller surfaces the error and the data stays in the source directory.
            MigrateDataFiles(_current, target);
            MigrateSavesDirectory(_current, target);
        }

        _current = target;
        WritePointer(target);

        ConfigJson.Write(_fileSystem, Path.Combine(target, ConfigFileNames.Settings), settings);
    }

    private void MigrateSavesDirectory(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string sourceSaves = Path.Combine(source, SaveGameService.SavesRootName);
        if (!_fileSystem.DirectoryExists(sourceSaves))
        {
            return;
        }

        try
        {
            // Merge into any existing Saves folder, then remove the source copy.
            _fileSystem.CopyDirectory(sourceSaves, Path.Combine(target, SaveGameService.SavesRootName));
            _fileSystem.DeleteDirectory(sourceSaves, recursive: true);
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to move the progress saves to {target}: {ex.Message}", ex);
        }
    }

    private void MigrateDataFiles(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (string fileName in DataFileNames)
        {
            string sourcePath = Path.Combine(source, fileName);
            if (!_fileSystem.FileExists(sourcePath))
            {
                continue;
            }

            try
            {
                string targetPath = Path.Combine(target, fileName);
                _fileSystem.CopyFile(sourcePath, targetPath);
                _fileSystem.DeleteFile(sourcePath);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to move {fileName} to {target}: {ex.Message}", ex);
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
