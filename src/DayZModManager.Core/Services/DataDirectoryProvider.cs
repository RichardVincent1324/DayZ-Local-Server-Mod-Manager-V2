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
        /// the bootstrap directory.
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
        private readonly IFileSystem _fileSystem;

        private string _current = string.Empty;

        public DataDirectoryProvider(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public string Current => _current;

        public void Initialize()
        {
            string bootstrap = AppPaths.BootstrapDirectory();
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

            // Bootstrap at the AppData anchor until a server path is configured; the
            // server-path derived default is adopted once an Apply relocates there.
            _current = bootstrap;
        }

        public string Resolve(Settings settings) =>
            AppPaths.Resolve(settings.ServerPath);

    public void MoveTo(string directory, Settings settings)
    {
        string target = Normalize(directory);
        _fileSystem.CreateDirectory(target);

        if (!string.Equals(target, _current, StringComparison.OrdinalIgnoreCase))
        {
            // A failed migration must never silently strand user data (the preset
            // hierarchy: server configuration, mod order, types configuration and
            // world saves) in a directory the app stops reading. Abort the
            // relocation before the pointer moves so the caller surfaces the error
            // and the data stays in the source directory.
            MigratePresetsDirectory(_current, target);
        }

        _current = target;
        WritePointer(target);

        ConfigJson.Write(_fileSystem, Path.Combine(target, ConfigFileNames.Settings), settings);
    }

    private void MigratePresetsDirectory(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string sourcePresets = Path.Combine(source, PresetPaths.PresetsDirectoryName);
        if (!_fileSystem.DirectoryExists(sourcePresets))
        {
            return;
        }

        try
        {
            // Merge into any existing Presets folder, then remove the source copy.
            _fileSystem.CopyDirectory(sourcePresets, Path.Combine(target, PresetPaths.PresetsDirectoryName));
            _fileSystem.DeleteDirectory(sourcePresets, recursive: true);
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to move the presets to {target}: {ex.Message}", ex);
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
            string bootstrap = AppPaths.BootstrapDirectory();
            _fileSystem.CreateDirectory(bootstrap);
            _fileSystem.WriteAllText(PointerPath(), directory);
        }
        catch (Exception)
        {
            // Best effort: the pointer is only used to rediscover the data
            // directory on startup; failing to write it must not break startup
            // or an Apply.
        }
    }

    private static string PointerPath() => Path.Combine(AppPaths.BootstrapDirectory(), AppPaths.PointerFileName);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(path.Trim());
}
