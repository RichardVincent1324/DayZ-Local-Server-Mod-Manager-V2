namespace DayZModManager.Core.Services;

/// <summary>
/// The complete launch environment of the active map and preset: the preset folder
/// to persist preset-scoped configuration into, and the values written to the
/// launch batch file. Resolved once by <see cref="IPresetService.ResolveEnvironment"/>
/// so Apply, map switching and the launch preflight all agree on the same paths.
/// </summary>
public sealed record ServerEnvironment
{
    public required string MapName { get; init; }

    public required string PresetName { get; init; }

    /// <summary>Absolute folder of the active preset.</summary>
    public required string PresetFolder { get; init; }

    /// <summary>Value for the batch file's <c>serverProfile</c> line (relative to the server root).</summary>
    public required string ServerProfile { get; init; }

    /// <summary>Value for the batch file's <c>serverConfig</c> line (relative to the server root).</summary>
    public required string ServerConfig { get; init; }

    /// <summary>The active preset's junction-folder key (its instanceId as a string).</summary>
    public required string JunctionKey { get; init; }
}

/// <summary>
/// Outcome of resolving a <see cref="ServerEnvironment"/>. Either
/// <see cref="Environment"/> is non-null or <see cref="Error"/> describes why no
/// usable environment could be resolved.
/// </summary>
public sealed record EnvironmentResolution(ServerEnvironment? Environment, string? Error)
{
    /// <summary>Non-fatal notices produced while resolving (for example, a healed default preset).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static EnvironmentResolution Resolved(
        ServerEnvironment environment, IReadOnlyList<string>? warnings = null) =>
        new(environment, null) { Warnings = warnings ?? Array.Empty<string>() };

    public static EnvironmentResolution Failed(string error) => new(null, error);
}
