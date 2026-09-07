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
/// Synchronizes junctions under the <see cref="ModListFolder.Name"/> folder in the
/// server root with the desired loaded-mod set. Junction work is split into a
/// non-destructive preparation phase (create missing links, validate targets) and
/// a destructive finalize phase (re-point stale links, remove orphaned junctions),
/// so an Apply can abort safely after preparation without tearing down the links a
/// still-unchanged launch batch file depends on. Junctions created directly in the
/// server root by older versions are left untouched.
/// </summary>
public interface IJunctionService
{
    /// <summary>
    /// Runs both junction phases (prepare then finalize) in one call. Provided for
    /// callers that want a single, complete synchronization.
    /// </summary>
    JunctionSyncResult Sync(string serverPath, string workshopPath, IReadOnlyList<string> loadedMods);

    /// <summary>
    /// Non-destructive preparation: ensures the ModList folder exists and creates
    /// junctions for loaded mods that have none, skipping links that already point
    /// at the right target. Links pointing elsewhere (e.g. a changed workshop path)
    /// are validated but NOT re-pointed here - that is deferred to
    /// <see cref="Finalize"/>. Orphaned junctions are never touched. Returns
    /// failures when a target cannot be created/resolved; callers should abort
    /// before mutating the batch file or configuration when <see cref="JunctionSyncResult.Failed"/>
    /// is non-zero.
    /// </summary>
    JunctionSyncResult PrepareLoaded(string serverPath, string workshopPath, IReadOnlyList<string> loadedMods);

    /// <summary>
    /// Destructive finalization: re-points loaded junctions whose target changed
    /// (after confirming the new target exists) and removes junctions for mods not
    /// in <paramref name="loadedMods"/>. Intended to run only after a successful
    /// batch-file/config commit. Failures are reported but are not fatal to the
    /// already-committed configuration (a stuck link is retried on the next Apply).
    /// </summary>
    JunctionSyncResult Finalize(string serverPath, string workshopPath, IReadOnlyList<string> loadedMods);

    /// <summary>Returns mod junction names under the ModList folder not present in <paramref name="validModNames"/>.</summary>
    IReadOnlyList<string> FindOrphanedJunctions(string serverPath, IReadOnlySet<string> validModNames);

    /// <summary>Returns the loaded mods whose junction is missing or not a junction.</summary>
    IReadOnlyList<string> Verify(string serverPath, IReadOnlyList<string> loadedMods);
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

    public JunctionSyncResult Sync(string serverPath, string workshopPath, IReadOnlyList<string> loadedMods)
    {
        JunctionSyncResult prepared = PrepareLoaded(serverPath, workshopPath, loadedMods);
        JunctionSyncResult finalized = Finalize(serverPath, workshopPath, loadedMods);

        return new JunctionSyncResult
        {
            Created = prepared.Created + finalized.Created,
            Removed = prepared.Removed + finalized.Removed,
            Skipped = prepared.Skipped + finalized.Skipped,
            Failed = prepared.Failed + finalized.Failed,
            Messages = prepared.Messages.Concat(finalized.Messages).ToList(),
        };
    }

    public JunctionSyncResult PrepareLoaded(string serverPath, string workshopPath, IReadOnlyList<string> loadedMods)
    {
        var messages = new List<string>();
        int created = 0, skipped = 0, failed = 0;

        string junctionDir = Path.Combine(serverPath, ModListFolder.Name);
        if (!EnsureJunctionDirectory(junctionDir, messages, ref failed))
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

    public JunctionSyncResult Finalize(string serverPath, string workshopPath, IReadOnlyList<string> loadedMods)
    {
        var messages = new List<string>();
        int created = 0, removed = 0, failed = 0;

        string junctionDir = Path.Combine(serverPath, ModListFolder.Name);
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

        // Remove junctions whose mod is no longer loaded. A failure here only
        // leaves a harmless orphan that the next Apply will retry.
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

    public IReadOnlyList<string> FindOrphanedJunctions(string serverPath, IReadOnlySet<string> validModNames)
    {
        return GetJunctionedMods(JunctionDir(serverPath))
            .Where(mod => !validModNames.Contains(mod))
            .ToList();
    }

    public IReadOnlyList<string> Verify(string serverPath, IReadOnlyList<string> loadedMods)
    {
        string junctionDir = JunctionDir(serverPath);
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

    /// <summary>Ensures the ModList junction folder exists, returning false when it cannot be created.</summary>
    private bool EnsureJunctionDirectory(string junctionDir, List<string> messages, ref int failed)
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

        messages.Add($"Mod folder created: {ModListFolder.Name}");
        return true;
    }

    private static string JunctionDir(string serverPath) => Path.Combine(serverPath, ModListFolder.Name);

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
