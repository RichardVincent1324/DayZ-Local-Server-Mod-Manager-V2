using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Input for an Apply operation.</summary>
public sealed record ApplyContext
{
    public required Settings Settings { get; init; }

    public required IReadOnlyList<string> LoadedMods { get; init; }

    /// <summary>Directory where configuration files are persisted.</summary>
    public required string DataDirectory { get; init; }
}

/// <summary>Outcome of an Apply operation, including ordered human-readable logs.</summary>
public sealed record ApplyResult
{
    public bool Success { get; init; }

    public IReadOnlyList<string> Logs { get; init; } = Array.Empty<string>();
}

    /// <summary>
    /// Synchronizes the desired configuration with the actual server. Sequence:
    /// validate, prepare junctions (non-destructive), update batch file, save
    /// config, finalize junctions (destructive), verify. A preparation failure
    /// aborts before the batch file or any configuration is touched; a batch-file
    /// failure aborts before configuration is persisted and before any junction is
    /// deleted or re-pointed, so a failed Apply never tears down the links the
    /// unchanged launch batch file still depends on. Destructive junction cleanup
    /// only runs after the configuration is committed and degrades to a warning
    /// when a link cannot be removed.
    /// </summary>
public interface IApplyService
{
    ApplyResult Apply(ApplyContext context);
}

public sealed class ApplyService : IApplyService
{
    private readonly ISettingsService _settings;
    private readonly IModOrderStore _modOrder;
    private readonly IBatchFileService _batchFile;
    private readonly IJunctionService _junctions;
    private readonly IValidationService _validation;
    private readonly IFileSystem _fileSystem;

    public ApplyService(
        ISettingsService settings,
        IModOrderStore modOrder,
        IBatchFileService batchFile,
        IJunctionService junctions,
        IValidationService validation,
        IFileSystem fileSystem)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _modOrder = modOrder ?? throw new ArgumentNullException(nameof(modOrder));
        _batchFile = batchFile ?? throw new ArgumentNullException(nameof(batchFile));
        _junctions = junctions ?? throw new ArgumentNullException(nameof(junctions));
        _validation = validation ?? throw new ArgumentNullException(nameof(validation));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ApplyResult Apply(ApplyContext context)
    {
        var logs = new List<string>();

        // 1. Validate
        IReadOnlyList<string> errors = _validation.Validate(new ValidationContext
        {
            Settings = context.Settings,
            LoadedMods = context.LoadedMods,
        });

        if (errors.Count > 0)
        {
            logs.Add($"Validation failed: {string.Join(" | ", errors)}");

            return new ApplyResult { Success = false, Logs = logs };
        }

        logs.Add("Validation passed.");

        // 2. Prepare junctions for the loaded mods. This phase is non-destructive:
        //    it creates missing junctions and validates that every target can be
        //    resolved, but it never deletes or re-points existing junctions. A
        //    failure aborts here so the batch file and configuration are never
        //    touched while a previously-working junction (or a still-referenced
        //    mod's link) is left broken.
        JunctionSyncResult prepared = _junctions.PrepareLoaded(
            context.Settings.ServerPath,
            context.Settings.WorkshopPath,
            context.LoadedMods);

        logs.AddRange(prepared.Messages);

        if (prepared.Failed > 0)
        {
            logs.Add("ERROR: Junction synchronization reported failures. No changes were made.");
            return new ApplyResult { Success = false, Logs = logs };
        }

        // 3. Update the batch file. A failure here aborts before any configuration
        //    is persisted AND before any destructive junction work (re-pointing or
        //    removing the links of unloaded mods), so the launch batch file and the
        //    junctions the current mod set depends on stay consistent with each
        //    other. Junctions created for newly-loaded mods are harmless and are
        //    reconciled again on the next Apply. The previous contents are kept so
        //    a later failure can restore the file instead of leaving it diverged.
        IReadOnlyList<string> modPaths = context.LoadedMods.Select(ModListFolder.Entry).ToList();
        string batchPath = context.Settings.BatFilePath;
        string? batchSnapshot = ReadFileSnapshot(batchPath);
        try
        {
            if (!_batchFile.WriteModList(batchPath, modPaths))
            {
                logs.Add("ERROR: Failed to update the batch file. No changes were made.");
                return new ApplyResult { Success = false, Logs = logs };
            }
        }
        catch (Exception ex)
        {
            RestoreFileSnapshot(batchPath, batchSnapshot);
            logs.Add($"ERROR: Failed to update the batch file: {ex.Message}. The launch batch file was not changed.");
            return new ApplyResult { Success = false, Logs = logs };
        }

        logs.Add("Batch file updated.");

        // 4. Save configuration. Persisting can still fail (disk full, permissions,
        //    locked data directory). If it does, restore the batch file so the
        //    launcher does not reference a mod list that was never committed.
        try
        {
            _settings.Save(context.DataDirectory, context.Settings);
            _modOrder.Save(context.DataDirectory, context.LoadedMods);
        }
        catch (Exception ex)
        {
            RestoreFileSnapshot(batchPath, batchSnapshot);
            logs.Add($"ERROR: Failed to save the configuration: {ex.Message}. The launch batch file was restored.");
            return new ApplyResult { Success = false, Logs = logs };
        }

        logs.Add($"Saved configuration: {context.LoadedMods.Count} mod(s) loaded.");

        // 5. Destructive junction finalization: re-point stale links and remove the
        //    junctions of mods that are no longer loaded. Runs only after the batch
        //    file and configuration are committed, so a stuck junction degrades to
        //    a warning (retried on the next Apply) instead of aborting the Apply.
        JunctionSyncResult finalized = _junctions.Finalize(
            context.Settings.ServerPath,
            context.Settings.WorkshopPath,
            context.LoadedMods);

        logs.AddRange(finalized.Messages);
        if (finalized.Failed > 0)
        {
            logs.Add($"WARNING: {finalized.Failed} junction operation(s) failed. Leftover junction(s) remain and will be retried on the next Apply.");
        }

        // 6. Verify
        IReadOnlyList<string> missing = _junctions.Verify(context.Settings.ServerPath, context.LoadedMods);
        if (missing.Count > 0)
        {
            logs.Add($"WARNING: {missing.Count} loaded mod(s) have no junction: {string.Join(", ", missing)}");
        }

        // 7. Report
        int created = prepared.Created + finalized.Created;
        int removed = prepared.Removed + finalized.Removed;
        int skipped = prepared.Skipped + finalized.Skipped;
        logs.Add($"Junctions: {created} created, {removed} removed, {skipped} skipped.");
        logs.Add("Apply complete.");
        return new ApplyResult { Success = true, Logs = logs };
    }

    /// <summary>Reads a file's contents for later rollback, or null when it cannot be read.</summary>
    private string? ReadFileSnapshot(string path)
    {
        try
        {
            return _fileSystem.FileExists(path) ? _fileSystem.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Restores a file from a snapshot, ignoring failures (best effort).</summary>
    private void RestoreFileSnapshot(string path, string? contents)
    {
        if (contents is null)
        {
            return;
        }

        try
        {
            _fileSystem.WriteAllText(path, contents);
        }
        catch (Exception)
        {
            // Best effort: the caller has already reported the Apply failure.
        }
    }
}
