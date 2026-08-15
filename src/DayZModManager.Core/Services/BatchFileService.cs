using System.Text.RegularExpressions;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// Reads and writes the manager-owned lines inside the user's launch batch file
/// (<c>modList</c> and <c>serverProfile</c>). Only those single lines are ever
/// changed; all other launch parameters are left untouched. Line endings are
/// preserved on write.
/// </summary>
public interface IBatchFileService
{
    /// <summary>
    /// Parses the <c>set "modList=..."</c> line and returns the ordered mod names.
    /// Returns an empty list when the file or the line is absent.
    /// </summary>
    IReadOnlyList<string> ReadModList(string batFilePath);

    /// <summary>Returns true if the file contains a <c>modList</c> line.</summary>
    bool HasModListLine(string batFilePath);

    /// <summary>
    /// Rewrites the <c>modList</c> line with the given ordered mods. Returns false
    /// if the file or the line is missing.
    /// </summary>
    bool WriteModList(string batFilePath, IReadOnlyList<string> modNames);

    /// <summary>
    /// Rewrites the <c>serverProfile</c> line with the given relative profile path.
    /// Returns false if the file or the line is missing.
    /// </summary>
    bool WriteServerProfile(string batFilePath, string relativeProfile);
}

public sealed partial class BatchFileService : IBatchFileService
{
    private readonly IFileSystem _fileSystem;

    public BatchFileService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public IReadOnlyList<string> ReadModList(string batFilePath)
    {
        if (string.IsNullOrWhiteSpace(batFilePath) || !_fileSystem.FileExists(batFilePath))
        {
            return Array.Empty<string>();
        }

        string content = _fileSystem.ReadAllText(batFilePath);
        Match match = ModListReadRegex().Match(content);
        if (!match.Success)
        {
            return Array.Empty<string>();
        }

        const string prefix = "-mod=";
        string value = match.Groups[1].Value;
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        return value[prefix.Length..]
            .Split(';')
            .Select(mod => mod.Trim())
            .Where(mod => mod.Length > 0)
            .ToList();
    }

    public bool HasModListLine(string batFilePath)
    {
        if (string.IsNullOrWhiteSpace(batFilePath) || !_fileSystem.FileExists(batFilePath))
        {
            return false;
        }

        return ModListLineRegex().IsMatch(_fileSystem.ReadAllText(batFilePath));
    }

    public bool WriteModList(string batFilePath, IReadOnlyList<string> modNames)
    {
        string modString = string.Join(';', modNames);
        if (modString.Length > 0)
        {
            modString += ';';
        }

        return ReplaceSingleLine(batFilePath, ModListLineRegex(), $"set \"modList=-mod={modString}\"");
    }

    public bool WriteServerProfile(string batFilePath, string relativeProfile)
    {
        return ReplaceSingleLine(batFilePath, ServerProfileLineRegex(), $"set \"serverProfile={relativeProfile}\"");
    }

    private bool ReplaceSingleLine(string filePath, Regex linePattern, string newLine)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !_fileSystem.FileExists(filePath))
        {
            return false;
        }

        string content = _fileSystem.ReadAllText(filePath);
        string[] lines = content.Split('\n');
        bool replaced = false;

        for (int i = 0; i < lines.Length; i++)
        {
            bool hadCarriageReturn = lines[i].EndsWith('\r');
            string line = hadCarriageReturn ? lines[i][..^1] : lines[i];

            if (linePattern.IsMatch(line))
            {
                lines[i] = newLine + (hadCarriageReturn ? "\r" : string.Empty);
                replaced = true;
            }
        }

        if (!replaced)
        {
            return false;
        }

        _fileSystem.WriteAllText(filePath, string.Join("\n", lines));
        return true;
    }

    [GeneratedRegex(@"^\s*set\s+""modList=(-mod=.*?)""\s*$", RegexOptions.Multiline)]
    private static partial Regex ModListReadRegex();

    [GeneratedRegex(@"^\s*set\s+""modList=.*""\s*$", RegexOptions.Multiline)]
    private static partial Regex ModListLineRegex();

    [GeneratedRegex(@"^\s*set\s+""serverProfile=.*""\s*$", RegexOptions.Multiline)]
    private static partial Regex ServerProfileLineRegex();
}
