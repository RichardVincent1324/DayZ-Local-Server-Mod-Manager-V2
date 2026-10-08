using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>Result of a junction synchronization operation.</summary>
public sealed record JunctionSyncResult
{
    public int Created { get; init; }

    public int Removed { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Synchronizes junctions under a preset's own folder
/// (<c>&lt;serverRoot&gt;\ModList\&lt;presetKey&gt;</c>) with that preset's desired
/// loaded-mod set. Because each preset has its own subfolder, synchronizing one
/// preset never touches another preset's junctions, so switching presets does not
/// recreate or delete links. Junction work is split into a non-destructive
/// preparation phase (create missing links, validate targets) and a destructive
/// finalize phase (re-point stale links, remove orphaned junctions), so an Apply
/// can abort safely after preparation without tearing down the links a
/// still-unchanged launch batch file depends on.
/// </summary>
public interface IJunctionService
{
    /// <summary>
    /// Runs both junction phases (prepare then finalize) in one call. Provided for
    /// callers that want a single, complete synchronization.
    /// </summary>
    JunctionSyncResult Sync(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods);

    /// <summary>
    /// Non-destructive preparation: ensures the preset's ModList folder exists and
    /// creates junctions for loaded mods that have none, skipping links that
    /// already point at the right target. Links pointing elsewhere (e.g. a changed
    /// workshop path) are validated but NOT re-pointed here - that is deferred to
    /// <see cref="Finalize"/>. Orphaned junctions are never touched. Returns
    /// failures when a target cannot be created/resolved; callers should abort
    /// before mutating the batch file or configuration when
    /// <see cref="JunctionSyncResult.Failed"/> is non-zero.
    /// </summary>
    JunctionSyncResult PrepareLoaded(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods);

    /// <summary>
    /// Destructive finalization: re-points loaded junctions whose target changed
    /// (after confirming the new target exists) and removes junctions for mods not
    /// in <paramref name="loadedMods"/> from this preset's folder only. Intended to
    /// run only after a successful batch-file/config commit. Failures are reported
    /// but are not fatal to the already-committed configuration (a stuck link is
    /// retried on the next Apply).
    /// </summary>
    JunctionSyncResult Finalize(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods);

    /// <summary>Returns mod junction names under the preset's folder not present in <paramref name="validModNames"/>.</summary>
    IReadOnlyList<string> FindOrphanedJunctions(string serverPath, string presetKey, IReadOnlySet<string> validModNames);

    /// <summary>Returns the loaded mods whose junction is missing or not a junction.</summary>
    IReadOnlyList<string> Verify(string serverPath, string presetKey, IReadOnlyList<string> loadedMods);

    /// <summary>
    /// Removes a preset's junction folder and every junction inside it. Used when a
    /// preset is deleted so it leaves no orphaned links behind. Never traverses
    /// into junction targets.
    /// </summary>
    void DeleteJunctionFolder(string serverPath, string presetKey);
}

public sealed class JunctionService : IJunctionService
{
    private readonly IFileSystem _fileSystem;
    private readonly IJunctionOperations _junctions;

    public JunctionService(IFileSystem fileSystem, IJunctionOperations junctions)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _junctions = junctions ?? throw new ArgumentNullException(nameof(junctions));
    }

    public JunctionSyncResult Sync(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods)
    {
        JunctionSyncResult prepared = PrepareLoaded(serverPath, workshopPath, presetKey, loadedMods);
        JunctionSyncResult finalized = Finalize(serverPath, workshopPath, presetKey, loadedMods);

        return new JunctionSyncResult
        {
            Created = prepared.Created + finalized.Created,
            Removed = prepared.Removed + finalized.Removed,
            Skipped = prepared.Skipped + finalized.Skipped,
            Failed = prepared.Failed + finalized.Failed,
            Messages = prepared.Messages.Concat(finalized.Messages).ToList(),
        };
    }

    public JunctionSyncResult PrepareLoaded(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods)
    {
        var messages = new List<string>();
        int created = 0, skipped = 0, failed = 0;

        string junctionDir = ModListFolder.Directory(serverPath, presetKey);
        if (!EnsureJunctionDirectory(junctionDir, presetKey, messages, ref failed))
        {
            return new JunctionSyncResult
            {
                Created = 0,
                Removed = 0,
                Skipped = 0,
                Failed = failed,
                Messages = messages,
            };
        }

        foreach (string mod in loadedMods)
        {
            string link = Path.Combine(junctionDir, mod);
            string target = Path.Combine(workshopPath, mod);

            if (_junctions.IsJunction(link))
            {
                string? current = _junctions.GetTarget(link);
                if (current is not null && PathsEqual(current, target))
                {
                    skipped++;
                    continue;
                }

                // The link points at a different target (e.g. the workshop path
                // changed). Re-pointing is destructive, so it is deferred to
                // Finalize; only verify now that the new target can be resolved so
                // a doomed Apply fails before the batch file is touched.
                if (!_fileSystem.DirectoryExists(target))
                {
                    failed++;
                    messages.Add($"Mod not found in workshop: {mod}");
                }

                continue;
            }

            if (_fileSystem.DirectoryExists(link))
            {
                failed++;
                messages.Add($"Physical folder conflict: {mod} (cannot create junction)");
                continue;
            }

            if (!_fileSystem.DirectoryExists(target))
            {
                failed++;
                messages.Add($"Mod not found in workshop: {mod}");
                continue;
            }

            if (_junctions.Create(link, target))
            {
                created++;
                messages.Add($"Junction created: {mod}");
            }
            else
            {
                failed++;
                messages.Add($"Failed to create junction: {mod}");
            }
        }

        return new JunctionSyncResult
        {
            Created = created,
            Removed = 0,
            Skipped = skipped,
            Failed = failed,
            Messages = messages,
        };
    }

    public JunctionSyncResult Finalize(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods)
    {
        var messages = new List<string>();
        int created = 0, removed = 0, failed = 0;

        string junctionDir = ModListFolder.Directory(serverPath, presetKey);
        if (!_fileSystem.DirectoryExists(junctionDir))
        {
            return new JunctionSyncResult
            {
                Created = 0,
                Removed = 0,
                Skipped = 0,
                Failed = 0,
                Messages = messages,
            };
        }

        // Re-point loaded junctions whose target changed now that the caller has
        // committed the configuration. The new target was validated during prepare.
        foreach (string mod in loadedMods)
        {
            string link = Path.Combine(junctionDir, mod);
            string target = Path.Combine(workshopPath, mod);

            if (!_junctions.IsJunction(link))
            {
                continue;
            }

            string? current = _junctions.GetTarget(link);
            if (current is not null && PathsEqual(current, target))
            {
                continue;
            }

            if (!_fileSystem.DirectoryExists(target))
            {
                failed++;
                messages.Add($"Mod not found in workshop: {mod}");
                continue;
            }

            if (!_junctions.Delete(link))
            {
                failed++;
                messages.Add($"Failed to remove stale junction: {mod}");
                continue;
            }

            messages.Add($"Junction re-targeted: {mod}");
            if (_junctions.Create(link, target))
            {
                created++;
            }
            else
            {
                failed++;
                messages.Add($"Failed to create junction: {mod}");
            }
        }

        // Remove junctions whose mod is no longer loaded, within this preset's
        // folder only. A failure here only leaves a harmless orphan that the next
        // Apply will retry.
        var loadedSet = new HashSet<string>(loadedMods, StringComparer.OrdinalIgnoreCase);
        foreach (string mod in GetJunctionedMods(junctionDir))
        {
            if (loadedSet.Contains(mod))
            {
                continue;
            }

            if (_junctions.Delete(Path.Combine(junctionDir, mod)))
            {
                removed++;
                messages.Add($"Junction removed: {mod}");
            }
            else
            {
                failed++;
                messages.Add($"Failed to remove junction: {mod}");
            }
        }

        return new JunctionSyncResult
        {
            Created = created,
            Removed = removed,
            Skipped = 0,
            Failed = failed,
            Messages = messages,
        };
    }

    public IReadOnlyList<string> FindOrphanedJunctions(string serverPath, string presetKey, IReadOnlySet<string> validModNames)
    {
        return GetJunctionedMods(ModListFolder.Directory(serverPath, presetKey))
            .Where(mod => !validModNames.Contains(mod))
            .ToList();
    }

    public IReadOnlyList<string> Verify(string serverPath, string presetKey, IReadOnlyList<string> loadedMods)
    {
        string junctionDir = ModListFolder.Directory(serverPath, presetKey);
        var missing = new List<string>();
        foreach (string mod in loadedMods)
        {
            if (!_junctions.IsJunction(Path.Combine(junctionDir, mod)))
            {
                missing.Add(mod);
            }
        }

        return missing;
    }

    public void DeleteJunctionFolder(string serverPath, string presetKey)
    {
        string junctionDir = ModListFolder.Directory(serverPath, presetKey);
        if (!_fileSystem.DirectoryExists(junctionDir))
        {
            return;
        }

        // Remove each junction link first (never traversing into its target), then
        // the (now empty) folder. Best effort: failures leave a harmless leftover.
        foreach (string mod in GetJunctionedMods(junctionDir))
        {
            _junctions.Delete(Path.Combine(junctionDir, mod));
        }

        try
        {
            _fileSystem.DeleteDirectory(junctionDir, recursive: false);
        }
        catch (Exception)
        {
            // Best effort: the caller has already removed the preset.
        }
    }

    /// <summary>Ensures the preset's ModList junction folder exists, returning false when it cannot be created.</summary>
    private bool EnsureJunctionDirectory(string junctionDir, string presetKey, List<string> messages, ref int failed)
    {
        if (_fileSystem.DirectoryExists(junctionDir))
        {
            return true;
        }

        try
        {
            _fileSystem.CreateDirectory(junctionDir);
        }
        catch (Exception ex)
        {
            failed++;
            messages.Add($"Failed to create mod folder: {junctionDir} ({ex.Message})");
            return false;
        }

        messages.Add($"Mod folder created: {ModListFolder.Name}/{presetKey}");
        return true;
    }

    private IReadOnlyList<string> GetJunctionedMods(string junctionDir)
    {
        if (!_fileSystem.DirectoryExists(junctionDir))
        {
            return Array.Empty<string>();
        }

        return _fileSystem
            .GetDirectories(junctionDir)
            .Where(name => name.StartsWith("@", StringComparison.Ordinal))
            .Where(name => _junctions.IsJunction(Path.Combine(junctionDir, name)))
            .ToList();
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a.Trim()),
            Path.TrimEndingDirectorySeparator(b.Trim()),
            StringComparison.OrdinalIgnoreCase);
}
