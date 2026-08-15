using System.Text.RegularExpressions;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// Edits <c>serverDZ.cfg</c>. Only the <c>template</c> line is ever changed.
/// </summary>
public interface IServerConfigService
{
    /// <summary>
    /// Sets the mission <c>template</c> to the given folder name (e.g.
    /// "dayzOffline.chernarusplus"). Returns false if the file or the line is missing.
    /// </summary>
    bool UpdateTemplate(string serverPath, string missionFolderName);
}

public sealed partial class ServerConfigService : IServerConfigService
{
    private readonly IFileSystem _fileSystem;

    public ServerConfigService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public bool UpdateTemplate(string serverPath, string missionFolderName)
    {
        string configPath = Path.Combine(serverPath, "serverDZ.cfg");
        if (string.IsNullOrWhiteSpace(missionFolderName) || !_fileSystem.FileExists(configPath))
        {
            return false;
        }

        string content = _fileSystem.ReadAllText(configPath);
        if (!TemplateRegex().IsMatch(content))
        {
            return false;
        }

        string updated = TemplateRegex().Replace(content, _ => $"template=\"{missionFolderName}\"", 1);
        _fileSystem.WriteAllText(configPath, updated);
        return true;
    }

    [GeneratedRegex(@"template\s*=\s*""[^""]*""")]
    private static partial Regex TemplateRegex();
}
