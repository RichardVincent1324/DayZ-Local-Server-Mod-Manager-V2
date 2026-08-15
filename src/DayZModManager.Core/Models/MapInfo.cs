namespace DayZModManager.Core.Models;

/// <summary>A discoverable DayZ mission (map) folder.</summary>
/// <param name="Name">Folder name, used as the stable config key and display name.</param>
/// <param name="Path">Full path to the mission folder.</param>
public sealed record MapInfo(string Name, string Path);
