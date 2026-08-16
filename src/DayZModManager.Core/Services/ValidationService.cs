using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Input for <see cref="IValidationService.Validate"/>.</summary>
public sealed record ValidationContext
{
    public required Settings Settings { get; init; }

    public required IReadOnlyList<string> LoadedMods { get; init; }
}

/// <summary>
/// Runs preflight checks before an Apply. Returns a list of human-readable error
/// messages; an empty list means the environment is ready.
/// </summary>
public interface IValidationService
{
    IReadOnlyList<string> Validate(ValidationContext context);
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
        ValidateServer(settings.ServerPath, errors);
        ValidateBatch(settings.BatFilePath, errors);

        // Only check individual mods when the workshop directory actually exists,
        // otherwise every loaded mod is reported missing on top of the real error.
        if (!string.IsNullOrWhiteSpace(settings.WorkshopPath) && _fileSystem.DirectoryExists(settings.WorkshopPath))
        {
            ValidateMods(settings.WorkshopPath, context.LoadedMods, errors);
        }

        return errors;
    }

    private void ValidateWorkshop(string workshopPath, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(workshopPath))
        {
            errors.Add("Workshop path is not set.");
        }
        else if (!_fileSystem.DirectoryExists(workshopPath))
        {
            errors.Add($"Workshop directory not found: {workshopPath}");
        }
    }

    private void ValidateServer(string serverPath, List<string> errors)
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

        if (!_fileSystem.FileExists(Path.Combine(serverPath, ServerConfig)))
        {
            errors.Add($"{ServerConfig} not found in the server directory.");
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
        }
        else if (!_batchFile.HasModListLine(batFilePath))
        {
            errors.Add($"Batch file does not contain a modList line: {batFilePath}");
        }
    }

    private void ValidateMods(string workshopPath, IReadOnlyList<string> loadedMods, List<string> errors)
    {
        foreach (string mod in loadedMods)
        {
            if (!_fileSystem.DirectoryExists(Path.Combine(workshopPath, mod)))
            {
                errors.Add($"Mod not found in workshop: {mod}");
            }
        }
    }
}
