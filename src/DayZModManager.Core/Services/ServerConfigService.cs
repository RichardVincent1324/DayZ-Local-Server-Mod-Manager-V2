using System.Text.RegularExpressions;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// Edits a DayZ <c>serverDZ.cfg</c>. Only the <c>template</c> and
/// <c>instanceId</c> lines are ever changed.
/// </summary>
public interface IServerConfigService
{
    /// <summary>
    /// Sets the mission <c>template</c> in the given <paramref name="serverConfigPath"/>
    /// to the given folder name (e.g. "dayzOffline.chernarusplus"). Returns false if
    /// the file or the line is missing.
    /// </summary>
    bool UpdateTemplate(string serverConfigPath, string missionFolderName);

    /// <summary>
    /// Sets the <c>instanceId</c> in the given <paramref name="serverConfigPath"/>.
    /// When the line is missing it is appended so a freshly seeded config always
    /// carries the preset's dedicated ID. Returns false when the file is missing.
    /// </summary>
    bool WriteInstanceId(string serverConfigPath, int instanceId);

    /// <summary>
    /// Reads the <c>instanceId</c> from a server configuration file, returning null
    /// when the file is missing/unreadable, the line is absent, or the value is not
    /// a positive integer. This is the value DayZ itself uses to locate
    /// <c>storage_&lt;instanceId&gt;</c>, so it is authoritative when a preset's
    /// metadata is unavailable.
    /// </summary>
    int? TryReadInstanceId(string serverConfigPath);
}

public sealed partial class ServerConfigService : IServerConfigService
{
    private readonly IFileSystem _fileSystem;

    public ServerConfigService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public bool UpdateTemplate(string serverConfigPath, string missionFolderName)
    {
        if (string.IsNullOrWhiteSpace(missionFolderName)
            || string.IsNullOrWhiteSpace(serverConfigPath)
            || !_fileSystem.FileExists(serverConfigPath))
        {
            return false;
        }

        string content = _fileSystem.ReadAllText(serverConfigPath);
        if (!TemplateRegex().IsMatch(content))
        {
            return false;
        }

        string updated = TemplateRegex().Replace(content, _ => $"template=\"{missionFolderName}\"", 1);
        _fileSystem.WriteAllText(serverConfigPath, updated);
        return true;
    }

    public bool WriteInstanceId(string serverConfigPath, int instanceId)
    {
        if (string.IsNullOrWhiteSpace(serverConfigPath) || !_fileSystem.FileExists(serverConfigPath))
        {
            return false;
        }

        string content = _fileSystem.ReadAllText(serverConfigPath);
        if (InstanceIdRegex().IsMatch(content))
        {
            content = InstanceIdRegex().Replace(content, _ => $"instanceId={instanceId};", 1);
        }
        else
        {
            // Preserve the file's dominant newline; append a valid statement.
            string newline = content.Contains("\r\n") ? "\r\n" : "\n";
            if (content.Length > 0 && !content.EndsWith('\n'))
            {
                content += newline;
            }

            content += $"instanceId={instanceId};{newline}";
        }

        _fileSystem.WriteAllText(serverConfigPath, content);
        return true;
    }

    public int? TryReadInstanceId(string serverConfigPath)
    {
        if (string.IsNullOrWhiteSpace(serverConfigPath) || !_fileSystem.FileExists(serverConfigPath))
        {
            return null;
        }

        string content;
        try
        {
            content = _fileSystem.ReadAllText(serverConfigPath);
        }
        catch (Exception)
        {
            return null;
        }

        Match match = InstanceIdValueRegex().Match(content);
        return match.Success && int.TryParse(match.Groups[1].Value, out int id) && id > 0
            ? id
            : null;
    }

    [GeneratedRegex(@"^\s*template\s*=\s*""[^""]*""", RegexOptions.Multiline)]
    private static partial Regex TemplateRegex();

    [GeneratedRegex(@"^\s*instanceId\s*=\s*\d+\s*;?", RegexOptions.Multiline)]
    private static partial Regex InstanceIdRegex();

    [GeneratedRegex(@"^\s*instanceId\s*=\s*(\d+)\s*;?", RegexOptions.Multiline)]
    private static partial Regex InstanceIdValueRegex();
}
