using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of writing the launch batch file.</summary>
public sealed record LaunchWriteResult
{
    public bool Success { get; init; }

    /// <summary>
    /// True when the batch file has a <c>serverConfig</c> line (or none was
    /// requested); false when the preset config had to be copied to the server
    /// root instead.
    /// </summary>
    public bool ServerConfigLineWritten { get; init; } = true;

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}

/// <summary>Outcome of writing the preset's server location (<c>serverProfile</c>, <c>serverConfig</c>).</summary>
public sealed record ServerLocationWriteResult
{
    public bool ServerProfileLineWritten { get; init; } = true;

    public bool ServerConfigLineWritten { get; init; } = true;

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Writes the manager-owned launch variables for an environment. Apply writes the
/// full set (<c>modList</c>, <c>serverProfile</c>, <c>serverConfig</c>), while a map
/// switch writes only the server location before the subsequent Apply writes the
/// rest. Sharing this writer keeps the two paths from drifting, in particular the
/// fallback that copies the preset's <c>serverDZ.cfg</c> to the server root when
/// the launch template has no <c>serverConfig</c> line.
/// </summary>
public interface ILaunchEnvironmentService
{
    LaunchWriteResult Write(Settings settings, ServerEnvironment? environment, IReadOnlyList<string> loadedMods);

    ServerLocationWriteResult WriteServerLocation(Settings settings, ServerEnvironment environment);
}

public sealed class LaunchEnvironmentService : ILaunchEnvironmentService
{
    private readonly IBatchFileService _batchFile;
    private readonly IFileSystem _fileSystem;

    public LaunchEnvironmentService(IBatchFileService batchFile, IFileSystem fileSystem)
    {
        _batchFile = batchFile ?? throw new ArgumentNullException(nameof(batchFile));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
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

        bool serverConfigLineWritten = true;
        if (environment is not null)
        {
            ServerLocationWriteResult location = WriteServerLocation(settings, environment);
            messages.AddRange(location.Messages);
            serverConfigLineWritten = location.ServerConfigLineWritten;
        }

        messages.Add("Batch file updated.");
        return new LaunchWriteResult
        {
            Success = true,
            ServerConfigLineWritten = serverConfigLineWritten,
            Messages = messages,
        };
    }

    public ServerLocationWriteResult WriteServerLocation(Settings settings, ServerEnvironment environment)
    {
        var messages = new List<string>();
        string batPath = settings.BatFilePath;

        bool profileWritten = true;
        if (!string.IsNullOrWhiteSpace(environment.ServerProfile))
        {
            profileWritten = _batchFile.WriteServerProfile(batPath, environment.ServerProfile);
            if (!profileWritten)
            {
                messages.Add("WARNING: The batch file has no serverProfile line; the preset's profiles folder will not be used.");
            }
        }

        bool configWritten = true;
        if (!string.IsNullOrWhiteSpace(environment.ServerConfig))
        {
            configWritten = _batchFile.WriteServerConfig(batPath, environment.ServerConfig);
            if (!configWritten)
            {
                messages.Add("WARNING: The batch file has no serverConfig line; the preset's serverDZ.cfg will be copied to the server root instead.");
                CopyPresetConfigToServerRoot(settings, environment, messages);
            }
        }

        return new ServerLocationWriteResult
        {
            ServerProfileLineWritten = profileWritten,
            ServerConfigLineWritten = configWritten,
            Messages = messages,
        };
    }

    /// <summary>
    /// Copies the preset's <c>serverDZ.cfg</c> over the server root config when the
    /// launch template has no <c>serverConfig</c> line, so the preset is still the
    /// environment the server runs.
    /// </summary>
    private void CopyPresetConfigToServerRoot(Settings settings, ServerEnvironment environment, List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(environment.PresetFolder)
            || string.IsNullOrWhiteSpace(settings.ServerPath))
        {
            return;
        }

        string presetConfig = Path.Combine(environment.PresetFolder, ConfigFileNames.ServerConfig);
        string rootConfig = Path.Combine(settings.ServerPath, ConfigFileNames.ServerConfig);
        if (!_fileSystem.FileExists(presetConfig)
            || string.Equals(presetConfig, rootConfig, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            _fileSystem.CopyFile(presetConfig, rootConfig);
            messages.Add("Copied the preset's serverDZ.cfg to the server root.");
        }
        catch (Exception ex)
        {
            messages.Add($"WARNING: Failed to copy the preset's serverDZ.cfg to the server root: {ex.Message}");
        }
    }
}
