using System.Text;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.IO;

/// <summary>Production <see cref="IFileSystem"/> backed by <see cref="System.IO"/>.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public bool DirectoryExists(string path)
        => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    public IReadOnlyList<string> GetDirectories(string path)
    {
        if (!DirectoryExists(path))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateDirectories(path)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();
    }

    public bool FileExists(string path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive)
    {
        if (!DirectoryExists(path))
        {
            return Array.Empty<string>();
        }

        SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(path, searchPattern, option).ToList();
    }

    public void CopyFile(string sourcePath, string destinationPath)
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            CreateDirectory(directory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    public void DeleteFile(string path)
    {
        if (FileExists(path))
        {
            File.Delete(path);
        }
    }

    public string ReadAllText(string path)
        => File.ReadAllText(path);

    public void WriteAllText(string path, string contents)
        => File.WriteAllText(path, contents, Utf8NoBom);

    public void CreateDirectory(string path)
        => Directory.CreateDirectory(path);

    public void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);
        foreach (string directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationPath, Path.GetRelativePath(sourcePath, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destinationPath, Path.GetRelativePath(sourcePath, file)), overwrite: true);
        }
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        if (DirectoryExists(path))
        {
            Directory.Delete(path, recursive);
        }
    }

    public void MoveDirectory(string sourcePath, string destinationPath)
        => Directory.Move(sourcePath, destinationPath);
}
