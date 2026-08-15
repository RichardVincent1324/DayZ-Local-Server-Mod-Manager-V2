namespace DayZModManager.Core.Models;

/// <summary>Outcome of loading a configuration file from disk.</summary>
public enum ConfigLoadStatus
{
    /// <summary>The file existed and was parsed successfully.</summary>
    Success,

    /// <summary>The file did not exist.</summary>
    Missing,

    /// <summary>The file existed but could not be parsed.</summary>
    Corrupt,
}

/// <summary>
/// Carries the outcome of a configuration load along with the parsed value
/// (when <see cref="Status"/> is <see cref="ConfigLoadStatus.Success"/>).
/// </summary>
public sealed record ConfigLoadResult<T>(ConfigLoadStatus Status, T? Value)
{
    public static ConfigLoadResult<T> Success(T value) => new(ConfigLoadStatus.Success, value);

    public static ConfigLoadResult<T> Missing() => new(ConfigLoadStatus.Missing, default);

    public static ConfigLoadResult<T> Corrupt() => new(ConfigLoadStatus.Corrupt, default);
}
