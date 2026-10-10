using System.Text.RegularExpressions;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// Locates the live DayZ world storage folders (<c>storage_&lt;instanceId&gt;</c>)
/// under a server's <c>mpmissions\&lt;mapName&gt;</c> directory. Shared by
/// <see cref="SaveGameService"/> and <see cref="PresetService"/> so the folder
/// convention has a single definition and instance IDs can be allocated without
/// colliding with storage the server created outside this tool.
/// </summary>
internal static partial class StorageFolders
{
    /// <summary>Matches a live storage folder leaf exactly, e.g. "storage_1".</summary>
    [GeneratedRegex(@"^storage_(\d+)$")]
    private static partial Regex NameRegex();

    /// <summary>
    /// Returns the instance IDs of the live <c>storage_&lt;id&gt;</c> folders present
    /// in the map's mission folder, ascending. Non-storage folders are excluded.
    /// Never throws.
    /// </summary>
    public static IReadOnlyList<int> ListInstanceIds(IFileSystem fileSystem, string serverPath, string mapName)
    {
        if (string.IsNullOrWhiteSpace(serverPath) || string.IsNullOrWhiteSpace(mapName))
        {
            return Array.Empty<int>();
        }

        string missionPath = Path.Combine(serverPath, "mpmissions", mapName);
        if (!fileSystem.DirectoryExists(missionPath))
        {
            return Array.Empty<int>();
        }

        var ids = new List<int>();
        try
        {
            foreach (string name in fileSystem.GetDirectories(missionPath))
            {
                Match match = NameRegex().Match(name);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
                {
                    ids.Add(id);
                }
            }
        }
        catch (Exception)
        {
            return Array.Empty<int>();
        }

        ids.Sort();
        return ids;
    }
}
