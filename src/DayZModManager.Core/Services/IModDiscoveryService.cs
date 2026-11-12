namespace DayZModManager.Core.Services;

/// <summary>
/// The outcome of a Workshop scan.
/// </summary>
/// <remarks>
/// <see cref="PathAvailable"/> separates "the folder was read successfully" from
/// "the folder could not be used". Without it, a blank, misspelled or temporarily
/// unavailable Workshop path looks exactly like a folder with no mods, and the
/// loaded list would be reported as entirely missing.
/// </remarks>
/// <param name="PathAvailable">True when the Workshop folder was read successfully.</param>
/// <param name="Mods">The discovered '@' prefixed mod folders, sorted by name.</param>
public sealed record ModDiscoveryResult(bool PathAvailable, IReadOnlyList<string> Mods)
{
    /// <summary>A scan that could not read the Workshop folder.</summary>
    public static ModDiscoveryResult Unavailable { get; } = new(false, Array.Empty<string>());
}

/// <summary>Discovers valid Steam Workshop mods (directories starting with '@').</summary>
public interface IModDiscoveryService
{
    /// <summary>
    /// Scans <paramref name="workshopPath"/> for '@' prefixed mod folders. When the
    /// path is blank or does not exist the result reports
    /// <see cref="ModDiscoveryResult.PathAvailable"/> as false so callers do not
    /// mistake an unusable path for an empty Workshop.
    /// </summary>
    ModDiscoveryResult DiscoverWorkshopMods(string workshopPath);
}
