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
    /// validate, synchronize junctions, update batch file, save config, verify.
    /// A junction failure aborts before the batch file or any configuration is
    /// touched; a batch-file failure aborts before any configuration is persisted
    /// so a failed Apply is never reloaded as the applied state on the next start.
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

    public ApplyService(
        ISettingsService settings,
        IModOrderStore modOrder,
        IBatchFileService batchFile,
        IJunctionService junctions,
        IValidationService validation)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _modOrder = modOrder ?? throw new ArgumentNullException(nameof(modOrder));
        _batchFile = batchFile ?? throw new ArgumentNullException(nameof(batchFile));
        _junctions = junctions ?? throw new ArgumentNullException(nameof(junctions));
        _validation = validation ?? throw new ArgumentNullException(nameof(validation));
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
            logs.Add("Validation failed:");
            foreach (string error in errors)
            {
                logs.Add($"  {error}");
            }

            return new ApplyResult { Success = false, Logs = logs };
        }

        logs.Add("Validation passed.");

        // 2. Synchronize junctions before touching the batch file or persisting
        //    any configuration: a junction failure must not modify the launch
        //    batch file or leave settings/mod_order.json written, otherwise a
        //    failed Apply would be reloaded as the applied state on the next start.
        JunctionSyncResult junctionResult = _junctions.Sync(
            context.Settings.ServerPath,
            context.Settings.WorkshopPath,
            context.LoadedMods);

        logs.AddRange(junctionResult.Messages);

        if (junctionResult.Failed > 0)
        {
            logs.Add("ERROR: Junction synchronization reported failures.");
            return new ApplyResult { Success = false, Logs = logs };
        }

        // 3. Update the batch file. A failure here aborts before any configuration
        //    is persisted. Junctions for the loaded mods already exist by now;
        //    they are harmless and are reconciled again on the next Apply.
        IReadOnlyList<string> modPaths = context.LoadedMods.Select(ModListFolder.Entry).ToList();
        if (!_batchFile.WriteModList(context.Settings.BatFilePath, modPaths))
        {
            logs.Add("ERROR: Failed to update the batch file. No changes were made.");
            return new ApplyResult { Success = false, Logs = logs };
        }

        logs.Add("Batch file updated.");

        // 4. Save configuration
        _settings.Save(context.DataDirectory, context.Settings);
        _modOrder.Save(context.DataDirectory, context.LoadedMods);
        logs.Add($"Saved configuration: {context.LoadedMods.Count} mod(s) loaded.");

        // 5. Verify
        IReadOnlyList<string> missing = _junctions.Verify(context.Settings.ServerPath, context.LoadedMods);
        if (missing.Count > 0)
        {
            logs.Add($"WARNING: {missing.Count} loaded mod(s) have no junction: {string.Join(", ", missing)}");
        }

        // 6. Report
        logs.Add($"Junctions: {junctionResult.Created} created, {junctionResult.Removed} removed, {junctionResult.Skipped} skipped.");
        logs.Add("Apply complete.");
        return new ApplyResult { Success = true, Logs = logs };
    }
}
