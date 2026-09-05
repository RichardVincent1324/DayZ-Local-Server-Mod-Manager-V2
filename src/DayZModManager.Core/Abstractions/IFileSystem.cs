namespace DayZModManager.Core.Abstractions;

/// <summary>
/// Minimal filesystem abstraction used to keep core services testable without
/// touching the real disk. Additional members are introduced only when a
/// subsystem actually needs them.
/// </summary>
public interface IFileSystem
{
    /// <summary>Returns true if the directory exists.</summary>
    bool DirectoryExists(string path);

    /// <summary>
    /// Returns the names (not full paths) of the immediate subdirectories of <paramref name="path"/>.
    /// Returns an empty list when the directory does not exist.
    /// </summary>
    IReadOnlyList<string> GetDirectories(string path);

    /// <summary>Returns true if the file exists.</summary>
    bool FileExists(string path);

    /// <summary>
    /// Returns the full paths of files under <paramref name="path"/> matching
    /// <paramref name="searchPattern"/>, optionally recursing into subdirectories.
    /// </summary>
    IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive);

    /// <summary>Copies a file, creating the destination directory if needed.</summary>
    void CopyFile(string sourcePath, string destinationPath);

    /// <summary>Deletes a file. Does nothing if the file does not exist.</summary>
    void DeleteFile(string path);

    /// <summary>Reads the entire file as text. Throws if the file does not exist.</summary>
    string ReadAllText(string path);

    /// <summary>Writes text to the file as UTF-8 without a byte-order mark.</summary>
    void WriteAllText(string path, string contents);

    /// <summary>Creates a directory (including parents). Idempotent.</summary>
    void CreateDirectory(string path);

    /// <summary>
    /// Recursively copies a directory tree to <paramref name="destinationPath"/>,
    /// creating the destination as needed. Overwrites existing files.
    /// </summary>
    void CopyDirectory(string sourcePath, string destinationPath);

    /// <summary>
    /// Deletes a directory. When <paramref name="recursive"/> is true the whole
    /// tree is removed; otherwise only an empty directory is deleted. Does nothing
    /// when the directory does not exist.
    /// </summary>
    void DeleteDirectory(string path, bool recursive);

    /// <summary>Moves (renames) a directory. The destination must not exist.</summary>
    void MoveDirectory(string sourcePath, string destinationPath);
}
