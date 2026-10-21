using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of writing the launch batch file.</summary>
public sealed record LaunchWriteResult
{
    public bool Success { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Writes the manager-owned launch variables for an environment. Apply writes the
/// full set (<c>modList</c>, <c>serverProfile</c>, <c>serverConfig</c>), while a map
/// switch writes only the server location before the subsequent Apply writes the
/// rest. Sharing this writer keeps the two paths from drifting.
/// <para>
/// A missing <c>serverProfile</c> or <c>serverConfig</c> line is a hard failure:
/// the environment cannot be pointed at the active preset. Nothing is written and
/// the server-root config is never modified as a fallback.
/// </para>
/// </summary>
public interface ILaunchEnvironmentService
{
    LaunchWriteResult Write(Settings settings, ServerEnvironment? environment, IReadOnlyList<string> loadedMods);

    LaunchWriteResult WriteServerLocation(Settings settings, ServerEnvironment environment);
}

public sealed class LaunchEnvironmentService : ILaunchEnvironmentService
{
    private readonly IBatchFileService _batchFile;

    public LaunchEnvironmentService(IBatchFileService batchFile)
    {
        _batchFile = batchFile ?? throw new ArgumentNullException(nameof(batchFile));
    }

    public LaunchWriteResult Write(Settings settings, ServerEnvironment? environment, IReadOnlyList<string> loadedMods)
    {
        var messages = new List<string>();

        // With no active environment there is nothing to point the launcher at, so
        // the mod list is cleared. This keeps a batch file from a previous map
        // pointing at mods that are no longer configured.
        IReadOnlyList<string> modPaths = environment is null
            ? Array.Empty<string>()
            : loadedMods.Select(mod => ModListFolder.Entry(environment.ModListKey, mod)).ToList();

        if (!_batchFile.WriteModList(settings.BatFilePath, modPaths))
        {
            messages.Add("ERROR: Failed to update the batch file. No changes were made.");
            return new LaunchWriteResult { Success = false, Messages = messages };
        }

        if (environment is not null)
        {
            LaunchWriteResult location = WriteServerLocation(settings, environment);
            messages.AddRange(location.Messages);
            if (!location.Success)
            {
                return new LaunchWriteResult { Success = false, Messages = messages };
            }
        }

        messages.Add("Batch file updated.");
        return new LaunchWriteResult { Success = true, Messages = messages };
    }

    public LaunchWriteResult WriteServerLocation(Settings settings, ServerEnvironment environment)
    {
        var messages = new List<string>();
        string batPath = settings.BatFilePath;

        // Validate both lines before writing anything so a non-conforming batch
        // file is rejected atomically. The preset is never pointed at the server
        // root config as a fallback.
        bool missingProfile = !string.IsNullOrWhiteSpace(environment.ServerProfile)
            && !_batchFile.HasServerProfileLine(batPath);
        bool missingConfig = !string.IsNullOrWhiteSpace(environment.ServerConfig)
            && !_batchFile.HasServerConfigLine(batPath);

        if (missingProfile)
        {
            messages.Add(
                $"ERROR: The batch file has no serverProfile line ({batPath}). " +
                "Add a set \"serverProfile=...\" line so the preset's profiles folder is used.");
        }

        if (missingConfig)
        {
            messages.Add(
                $"ERROR: The batch file has no serverConfig line ({batPath}). " +
                "Add a set \"serverConfig=...\" line so the preset's serverDZ.cfg is used.");
        }

        if (missingProfile || missingConfig)
        {
            return new LaunchWriteResult { Success = false, Messages = messages };
        }

        if (!string.IsNullOrWhiteSpace(environment.ServerProfile)
            && !_batchFile.WriteServerProfile(batPath, environment.ServerProfile))
        {
            messages.Add($"ERROR: Failed to update the serverProfile line in {batPath}.");
            return new LaunchWriteResult { Success = false, Messages = messages };
        }

        if (!string.IsNullOrWhiteSpace(environment.ServerConfig)
            && !_batchFile.WriteServerConfig(batPath, environment.ServerConfig))
        {
            messages.Add($"ERROR: Failed to update the serverConfig line in {batPath}.");
            return new LaunchWriteResult { Success = false, Messages = messages };
        }

        return new LaunchWriteResult { Success = true, Messages = messages };
    }
}
