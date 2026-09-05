using System.IO.Enumeration;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Tests.TestDoubles;

/// <summary>
/// In-memory <see cref="IFileSystem"/>. Directories are modelled as a map of
/// parent path to the set of immediate child directory names; files as a map of
/// path to text contents.
/// </summary>
public sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, HashSet<string>> _directories =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _files =
        new(StringComparer.OrdinalIgnoreCase);

    public bool DirectoryExists(string path) => _directories.ContainsKey(path);

    public IReadOnlyList<string> GetDirectories(string path) =>
        _directories.TryGetValue(path, out HashSet<string>? names)
            ? names.ToList()
            : Array.Empty<string>();

    public bool FileExists(string path) => _files.ContainsKey(path);

    public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive)
    {
        string prefix = EnsureTrailingSeparator(path);
        return _files.Keys
            .Where(f => recursive
                ? f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                : ParentOf(f).Equals(path, StringComparison.OrdinalIgnoreCase))
            .Where(f => FileSystemName.MatchesSimpleExpression(searchPattern, Path.GetFileName(f)))
            .ToList();
    }

    public void CopyFile(string sourcePath, string destinationPath)
    {
        if (!_files.TryGetValue(sourcePath, out string? contents))
        {
            throw new FileNotFoundException("File not found in fake filesystem.", sourcePath);
        }

        string? parent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(parent))
        {
            CreateDirectory(parent);
        }

        _files[destinationPath] = contents;
    }

    public void DeleteFile(string path) => _files.Remove(path);

    public string ReadAllText(string path) =>
        _files.TryGetValue(path, out string? contents)
            ? contents
            : throw new FileNotFoundException("File not found in fake filesystem.", path);

    public void WriteAllText(string path, string contents) => _files[path] = contents;

    public void CreateDirectory(string path)
    {
        if (!_directories.ContainsKey(path))
        {
            _directories[path] = new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public void CopyDirectory(string sourcePath, string destinationPath)
    {
        if (!DirectoryExists(sourcePath))
        {
            return;
        }

        CreateDirectory(destinationPath);
        string sourcePrefix = EnsureTrailingSeparator(sourcePath);

        foreach (string directory in _directories.Keys
                     .Where(d => d.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            string relative = directory[sourcePath.Length..].TrimStart('\\', '/');
            CreateDirectory(Path.Combine(destinationPath, relative));
        }

        foreach (string file in _files.Keys
                     .Where(f => f.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            string relative = file[sourcePath.Length..].TrimStart('\\', '/');
            _files[Path.Combine(destinationPath, relative)] = _files[file];
        }
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        string prefix = EnsureTrailingSeparator(path);

        foreach (string file in _files.Keys
                     .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            _files.Remove(file);
        }

        foreach (string directory in _directories.Keys
                     .Where(d => d.Equals(path, StringComparison.OrdinalIgnoreCase)
                         || d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            _directories.Remove(directory);
        }
    }

    public void MoveDirectory(string sourcePath, string destinationPath)
    {
        CopyDirectory(sourcePath, destinationPath);
        DeleteDirectory(sourcePath, recursive: true);
    }

    // --- Test helpers -----------------------------------------------------

    public void AddDirectory(string path, params string[] childDirectoryNames)
    {
        CreateDirectory(path);

        foreach (string child in childDirectoryNames)
        {
            _directories[path].Add(child);

            // Register the child as a real directory so DirectoryExists(childFullPath) works.
            CreateDirectory(Path.Combine(path, child));
        }
    }

    public void AddFile(string path, string contents) => _files[path] = contents;

    public string? TryGetFileContents(string path) =>
        _files.TryGetValue(path, out string? contents) ? contents : null;

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static string ParentOf(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        return dir ?? string.Empty;
    }
}
