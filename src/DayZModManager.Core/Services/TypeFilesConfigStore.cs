using System.Text.Json;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>
/// Loads and saves a preset's <c>type_files.json</c>. The file is a simple
/// dictionary of filename to economy role ("types" / "spawnabletypes"). The
/// filesystem determines which files exist; this store only records roles.
/// </summary>
public interface ITypeFilesConfigStore
{
    ConfigLoadResult<Dictionary<string, string>> Load(string presetFolder);

    /// <summary>
    /// Writes the role assignments, pruning entries whose files no longer exist.
    /// The save is aborted (throws) when the type_files folder cannot be scanned,
    /// so a locked/inaccessible directory cannot silently wipe valid assignments.
    /// </summary>
    void Save(string presetFolder, IReadOnlyDictionary<string, string> assignments);

    /// <summary>
    /// Preserves an unreadable <c>type_files.json</c> as <c>type_files.json.corrupt</c>
    /// before it is replaced. Returns true when the file is backed up.
    /// </summary>
    bool BackupCorrupt(string presetFolder);
}

public sealed class TypeFilesConfigStore : ITypeFilesConfigStore
{
    private readonly IFileSystem _fileSystem;

    public TypeFilesConfigStore(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ConfigLoadResult<Dictionary<string, string>> Load(string presetFolder)
    {
        string path = ConfigPath(presetFolder);
        if (!_fileSystem.FileExists(path))
        {
            return ConfigLoadResult<Dictionary<string, string>>.Missing();
        }

        return TryRead(path, out Dictionary<string, string>? assignments)
            ? ConfigLoadResult<Dictionary<string, string>>.Success(assignments!)
            : ConfigLoadResult<Dictionary<string, string>>.Corrupt();
    }

    public void Save(string presetFolder, IReadOnlyDictionary<string, string> assignments)
    {
        string typeFilesFolder = Path.Combine(presetFolder, PresetPaths.TypeFilesDirectoryName);
        IReadOnlySet<string> existing = ScanTypeFiles(typeFilesFolder);

        var cleaned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string fileName, string role) in assignments)
        {
            if (string.IsNullOrWhiteSpace(fileName) || !existing.Contains(fileName))
            {
                continue;
            }

            if (TypesFileRoles.TryNormalize(role, out string normalized))
            {
                cleaned[fileName] = normalized;
            }
        }

        string path = ConfigPath(presetFolder);

        // Never overwrite a corrupt file without preserving it first.
        if (_fileSystem.FileExists(path)
            && !TryRead(path, out _)
            && !BackupCorrupt(presetFolder))
        {
            throw new IOException(
                $"Refusing to overwrite corrupt {ConfigFileNames.TypeFilesConfig} because it could not be backed up: {path}");
        }

        ConfigJson.Write(_fileSystem, path, cleaned);
    }

    public bool BackupCorrupt(string presetFolder)
    {
        string sourcePath = ConfigPath(presetFolder);
        if (!_fileSystem.FileExists(sourcePath) || TryRead(sourcePath, out _))
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

    private string ConfigPath(string presetFolder) =>
        Path.Combine(presetFolder, ConfigFileNames.TypeFilesConfig);

    /// <summary>
    /// Returns the XML files directly inside type_files. Throws when the folder is
    /// missing or cannot be enumerated, so callers never prune against an empty
    /// result caused by a failed scan.
    /// </summary>
    private IReadOnlySet<string> ScanTypeFiles(string typeFilesFolder)
    {
        if (!_fileSystem.DirectoryExists(typeFilesFolder))
        {
            throw new IOException(
                $"Cannot verify the type_files folder because it does not exist: {typeFilesFolder}. " +
                "The assignments were not saved.");
        }

        IReadOnlyList<string> files;
        try
        {
            files = _fileSystem.GetFiles(typeFilesFolder, "*.xml", recursive: false);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Cannot verify the type_files folder because it could not be read: {typeFilesFolder} ({ex.Message}). " +
                "The assignments were not saved.",
                ex);
        }

        return files
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool TryRead(string path, out Dictionary<string, string>? assignments)
    {
        assignments = null;

        try
        {
            string raw = _fileSystem.ReadAllText(path);
            using JsonDocument doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (TypesFileRoles.TryNormalize(property.Value.GetString(), out string normalized))
                {
                    result[property.Name] = normalized;
                }
            }

            assignments = result;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
