using System.Text;
using System.Text.RegularExpressions;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// Reads and writes the manager-owned lines inside the user's launch batch file
/// (<c>modList</c> and <c>serverProfile</c>). Only those lines are ever changed;
/// all other launch parameters are left untouched. Line endings are preserved on
/// write.
/// <para>
/// The <c>modList</c> value can be long (100+ mods), so it is written as several
/// physical <c>set</c> lines, ten entries per line, each appending to the
/// variable (<c>set "modList=%modList%..."</c>). A single cmd <c>set</c> cannot
/// span physical lines (a trailing <c>^</c> does not continue a quoted value),
/// so appending keeps every line a complete, valid command.
/// </para>
/// </summary>
public interface IBatchFileService
{
    /// <summary>
    /// Parses the <c>modList</c> block and returns the ordered mod paths as
    /// written (e.g. <c>ModList/@CF</c>). Returns an empty list when the file or
    /// the line is absent.
    /// </summary>
    IReadOnlyList<string> ReadModList(string batFilePath);

    /// <summary>Returns true if the file contains a <c>modList</c> line.</summary>
    bool HasModListLine(string batFilePath);

    /// <summary>
    /// Rewrites the <c>modList</c> block with the given ordered mod paths,
    /// ten per physical line. Returns false if the file or the line is missing.
    /// </summary>
    bool WriteModList(string batFilePath, IReadOnlyList<string> modPaths);

    /// <summary>
    /// Rewrites the <c>serverProfile</c> line with the given relative profile path.
    /// Returns false if the file or the line is missing.
    /// </summary>
    bool WriteServerProfile(string batFilePath, string relativeProfile);
}

public sealed partial class BatchFileService : IBatchFileService
{
    /// <summary>Maximum number of mod entries written per physical line.</summary>
    public const int ModsPerLine = 10;

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

        string[] lines = _fileSystem.ReadAllText(batFilePath).Split('\n');
        int start = FindFirstLine(lines, ModListFirstLineValueRegex());
        if (start < 0)
        {
            return Array.Empty<string>();
        }

        var value = new StringBuilder();
        for (int i = start; i < lines.Length; i++)
        {
            string line = TrimCarriageReturn(lines[i]);

            if (i == start)
            {
                Match first = ModListFirstLineValueRegex().Match(line);
                if (!first.Success)
                {
                    return Array.Empty<string>();
                }

                value.Append(first.Groups[1].Value);
                continue;
            }

            Match append = ModListAppendLineValueRegex().Match(line);
            if (!append.Success)
            {
                break;
            }

            value.Append(append.Groups[1].Value);
        }

        return value.ToString()
            .Split(';')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();
    }

    public bool HasModListLine(string batFilePath)
    {
        if (string.IsNullOrWhiteSpace(batFilePath) || !_fileSystem.FileExists(batFilePath))
        {
            return false;
        }

        return StartLineRegex().IsMatch(_fileSystem.ReadAllText(batFilePath));
    }

    public bool WriteModList(string batFilePath, IReadOnlyList<string> modPaths)
    {
        return ReplaceModListBlock(batFilePath, BuildModListLines(modPaths));
    }

    public bool WriteServerProfile(string batFilePath, string relativeProfile)
    {
        return ReplaceSingleLine(batFilePath, ServerProfileLineRegex(), $"set \"serverProfile={relativeProfile}\"");
    }

    /// <summary>
    /// Builds the physical <c>set</c> lines for a mod list, ten entries per line.
    /// The first line starts with <c>-mod=</c>; each following line appends to the
    /// variable so the final value is one contiguous <c>;</c>-separated list.
    /// </summary>
    internal static List<string> BuildModListLines(IReadOnlyList<string> modPaths)
    {
        var lines = new List<string>();
        if (modPaths.Count == 0)
        {
            lines.Add("set \"modList=-mod=\"");
            return lines;
        }

        bool isFirst = true;
        for (int offset = 0; offset < modPaths.Count; offset += ModsPerLine)
        {
            int count = Math.Min(ModsPerLine, modPaths.Count - offset);
            string body = string.Join(';', modPaths.Skip(offset).Take(count)) + ";";
            lines.Add(isFirst
                ? $"set \"modList=-mod={body}\""
                : $"set \"modList=%modList%{body}\"");
            isFirst = false;
        }

        return lines;
    }

    /// <summary>
    /// Replaces the existing <c>modList</c> block (a start line plus any contiguous
    /// append lines) with <paramref name="newPhysicalLines"/>. Returns false if the
    /// file or the start line is missing.
    /// </summary>
    private bool ReplaceModListBlock(string filePath, IReadOnlyList<string> newPhysicalLines)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !_fileSystem.FileExists(filePath))
        {
            return false;
        }

        string content = _fileSystem.ReadAllText(filePath);
        string[] lines = content.Split('\n');
        int start = FindFirstLine(lines, StartLineRegex());
        if (start < 0)
        {
            return false;
        }

        bool carriageReturn = lines[start].EndsWith('\r');

        int end = start;
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (!AppendLineRegex().IsMatch(TrimCarriageReturn(lines[i])))
            {
                break;
            }

            end = i;
        }

        var output = new List<string>(lines.Length - (end - start) + newPhysicalLines.Count);
        for (int i = 0; i < start; i++)
        {
            output.Add(lines[i]);
        }

        foreach (string line in newPhysicalLines)
        {
            output.Add(carriageReturn ? line + "\r" : line);
        }

        for (int i = end + 1; i < lines.Length; i++)
        {
            output.Add(lines[i]);
        }

        _fileSystem.WriteAllText(filePath, string.Join("\n", output));
        return true;
    }

    private int FindFirstLine(string[] lines, Regex pattern)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (pattern.IsMatch(TrimCarriageReturn(lines[i])))
            {
                return i;
            }
        }

        return -1;
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

    private static string TrimCarriageReturn(string line) => line.EndsWith('\r') ? line[..^1] : line;

    /// <summary>Matches the first line of a modList block: <c>set "modList=-mod=...</c>.</summary>
    [GeneratedRegex(@"^\s*set\s+""modList=-mod=", RegexOptions.Multiline)]
    private static partial Regex StartLineRegex();

    /// <summary>Matches a continuation line: <c>set "modList=%modList%...</c>.</summary>
    [GeneratedRegex(@"^\s*set\s+""modList=%modList%", RegexOptions.Multiline)]
    private static partial Regex AppendLineRegex();

    /// <summary>Captures the value of the first modList line (after <c>-mod=</c>).</summary>
    [GeneratedRegex(@"^\s*set\s+""modList=-mod=(.*)""\s*$")]
    private static partial Regex ModListFirstLineValueRegex();

    /// <summary>Captures the appended value of a continuation modList line.</summary>
    [GeneratedRegex(@"^\s*set\s+""modList=%modList%(.*)""\s*$")]
    private static partial Regex ModListAppendLineValueRegex();

    [GeneratedRegex(@"^\s*set\s+""serverProfile=.*""\s*$", RegexOptions.Multiline)]
    private static partial Regex ServerProfileLineRegex();
}
