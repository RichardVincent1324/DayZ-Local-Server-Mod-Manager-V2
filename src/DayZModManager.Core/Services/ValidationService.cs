using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Input for <see cref="IValidationService.Validate"/>.</summary>
public sealed record ValidationContext
{
    public required Settings Settings { get; init; }

    public required IReadOnlyList<string> LoadedMods { get; init; }

    /// <summary>
    /// Path to the active preset's <c>serverDZ.cfg</c>. When set, it is validated
    /// as the server configuration; when null, the server root's <c>serverDZ.cfg</c>
    /// is validated instead.
    /// </summary>
    public string? ActiveServerConfigPath { get; init; }
}

/// <summary>
/// Runs preflight checks before an Apply. Returns a list of human-readable error
/// messages; an empty list means the environment is ready.
/// </summary>
public interface IValidationService
{
    IReadOnlyList<string> Validate(ValidationContext context);

    /// <summary>
    /// Returns the loaded mods that are absent from the Workshop directory. Apply
    /// tolerates missing mods (their junctions are simply skipped), so this is used
    /// only by the launch preflight to block a server launch. Returns an empty list
    /// when the Workshop path is unset or does not exist (the Workshop error is
    /// reported by <see cref="Validate"/> in that case).
    /// </summary>
    IReadOnlyList<string> FindMissingMods(string workshopPath, IReadOnlyList<string> loadedMods);
}

public sealed class ValidationService : IValidationService
{
    private const string ServerExecutable = "DayZServer_x64.exe";
    private const string ServerConfig = "serverDZ.cfg";

    private readonly IFileSystem _fileSystem;
    private readonly IBatchFileService _batchFile;

    public ValidationService(IFileSystem fileSystem, IBatchFileService batchFile)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _batchFile = batchFile ?? throw new ArgumentNullException(nameof(batchFile));
    }

    public IReadOnlyList<string> Validate(ValidationContext context)
    {
        var errors = new List<string>();
        Settings settings = context.Settings;

        ValidateWorkshop(settings.WorkshopPath, errors);
        ValidateServer(settings.ServerPath, context.ActiveServerConfigPath, errors);
        ValidateBatch(settings.BatFilePath, errors);

        return errors;
    }

    public IReadOnlyList<string> FindMissingMods(string workshopPath, IReadOnlyList<string> loadedMods)
    {
        if (string.IsNullOrWhiteSpace(workshopPath) || !_fileSystem.DirectoryExists(workshopPath))
        {
            return Array.Empty<string>();
        }

        return loadedMods
            .Where(mod => !_fileSystem.DirectoryExists(Path.Combine(workshopPath, mod)))
            .ToList();
    }

    private void ValidateWorkshop(string workshopPath, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(workshopPath))
        {
            errors.Add("Workshop path is not set.");
            return;
        }

        if (!_fileSystem.DirectoryExists(workshopPath))
        {
            errors.Add($"Workshop directory not found: {workshopPath}");
            return;
        }

        if (!WorkshopPathResolver.IsValidWorkshopPath(_fileSystem, workshopPath))
        {
            errors.Add(
                "Workshop path is not a valid DayZ Workshop or mods folder (choose the DayZ " +
                "\"!Workshop\" folder, the DayZ game folder, or a folder that directly contains " +
                $"\"@...\" mod folders): {workshopPath}");
        }
    }

    private void ValidateServer(string serverPath, string? activeServerConfigPath, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(serverPath))
        {
            errors.Add("Server path is not set.");
            return;
        }

        if (!_fileSystem.DirectoryExists(serverPath))
        {
            errors.Add($"Server directory not found: {serverPath}");
            return;
        }

        if (!_fileSystem.FileExists(Path.Combine(serverPath, ServerExecutable)))
        {
            errors.Add($"{ServerExecutable} not found in the server directory.");
        }

        string configPath = string.IsNullOrWhiteSpace(activeServerConfigPath)
            ? Path.Combine(serverPath, ServerConfig)
            : activeServerConfigPath;
        if (!_fileSystem.FileExists(configPath))
        {
            errors.Add($"{ServerConfig} not found: {configPath}");
        }
    }

    private void ValidateBatch(string batFilePath, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(batFilePath))
        {
            errors.Add("Batch file path is not set.");
            return;
        }

        if (!_fileSystem.FileExists(batFilePath))
        {
            errors.Add($"Batch file not found: {batFilePath}");
            return;
        }

        if (!_batchFile.HasModListLine(batFilePath))
        {
            errors.Add($"Batch file is missing a modList line: {batFilePath}");
        }

        if (!_batchFile.HasServerProfileLine(batFilePath))
        {
            errors.Add($"Batch file is missing a serverProfile line: {batFilePath}");
        }

        if (!_batchFile.HasServerConfigLine(batFilePath))
        {
            errors.Add($"Batch file is missing a serverConfig line: {batFilePath}");
        }
    }
}
