using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// Maintains the single mission-local junction that exposes the active preset's
/// <c>type_files</c> folder inside <c>mpmissions\&lt;map&gt;\db</c>. The junction
/// path is stable per map; switching presets re-points it instead of creating a
/// second link. This is deliberately separate from the Workshop mod junctions,
/// which have a different target and lifecycle, but reuses the same low-level
/// junction operations and prepare/finalize failure pattern as
/// <see cref="IJunctionService"/>.
/// </summary>
public interface ITypeFileJunctionService
{
    /// <summary>
    /// Validates the target folder and creates the junction when it is missing.
    /// A junction pointing at a different target is not changed here; that
    /// destructive step is deferred to <see cref="Finalize"/>. Returns failures
    /// when the target cannot be created or the link cannot be created.
    /// </summary>
    JunctionSyncResult Prepare(string missionPath, string targetFolder);

    /// <summary>
    /// Re-points an existing junction whose target changed, after validating the
    /// new target. On Windows this is a remove-then-create operation. Removing the
    /// old link only happens once the new target is confirmed to exist.
    /// </summary>
    JunctionSyncResult Finalize(string missionPath, string targetFolder);

    /// <summary>Runs <see cref="Prepare"/> then <see cref="Finalize"/>.</summary>
    JunctionSyncResult Sync(string missionPath, string targetFolder);

    /// <summary>True when the junction exists and points at an existing <paramref name="targetFolder"/>.</summary>
    bool Verify(string missionPath, string targetFolder);
}

public sealed class TypeFileJunctionService : ITypeFileJunctionService
{
    private readonly IFileSystem _fileSystem;
    private readonly IJunctionOperations _junctions;

    public TypeFileJunctionService(IFileSystem fileSystem, IJunctionOperations junctions)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _junctions = junctions ?? throw new ArgumentNullException(nameof(junctions));
    }

    public JunctionSyncResult Sync(string missionPath, string targetFolder)
    {
        JunctionSyncResult prepared = Prepare(missionPath, targetFolder);
        JunctionSyncResult finalized = Finalize(missionPath, targetFolder);

        return new JunctionSyncResult
        {
            Created = prepared.Created + finalized.Created,
            Removed = prepared.Removed + finalized.Removed,
            Skipped = prepared.Skipped + finalized.Skipped,
            Failed = prepared.Failed + finalized.Failed,
            Messages = prepared.Messages.Concat(finalized.Messages).ToList(),
        };
    }

    public JunctionSyncResult Prepare(string missionPath, string targetFolder)
    {
        var messages = new List<string>();
        int created = 0, skipped = 0, failed = 0;

        if (!EnsureTarget(missionPath, targetFolder, messages, ref failed))
        {
            return Result(created, 0, skipped, failed, messages);
        }

        string link = MissionTypeFiles.JunctionPath(missionPath);
        if (_junctions.IsJunction(link))
        {
            string? current = _junctions.GetTarget(link);
            if (current is not null && PathsEqual(current, targetFolder))
            {
                skipped++;
            }
            else
            {
                skipped++;
                messages.Add("Type files junction target change detected; re-pointing scheduled for apply.");
            }

            return Result(created, 0, skipped, failed, messages);
        }

        if (_fileSystem.DirectoryExists(link))
        {
            failed++;
            messages.Add($"Physical folder conflict: {link} (cannot create junction)");
            return Result(created, 0, skipped, failed, messages);
        }

        if (_junctions.Create(link, targetFolder))
        {
            created++;
            messages.Add("Type files junction created.");
        }
        else
        {
            failed++;
            messages.Add("Failed to create the type files junction.");
        }

        return Result(created, 0, skipped, failed, messages);
    }

    public JunctionSyncResult Finalize(string missionPath, string targetFolder)
    {
        var messages = new List<string>();
        int created = 0, removed = 0, skipped = 0, failed = 0;

        if (!EnsureTarget(missionPath, targetFolder, messages, ref failed))
        {
            return Result(created, removed, skipped, failed, messages);
        }

        string link = MissionTypeFiles.JunctionPath(missionPath);
        if (_junctions.IsJunction(link))
        {
            string? current = _junctions.GetTarget(link);
            if (current is not null && PathsEqual(current, targetFolder))
            {
                skipped++;
                return Result(created, removed, skipped, failed, messages);
            }

            if (!_junctions.Delete(link))
            {
                failed++;
                messages.Add("Failed to remove the stale type files junction.");
                return Result(created, removed, skipped, failed, messages);
            }

            removed++;
            if (_junctions.Create(link, targetFolder))
            {
                created++;
                messages.Add("Type files junction re-targeted.");
            }
            else
            {
                failed++;
                messages.Add("Failed to create the type files junction.");
            }

            return Result(created, removed, skipped, failed, messages);
        }

        if (_fileSystem.DirectoryExists(link))
        {
            failed++;
            messages.Add($"Physical folder conflict: {link} (cannot create junction)");
            return Result(created, removed, skipped, failed, messages);
        }

        if (_junctions.Create(link, targetFolder))
        {
            created++;
            messages.Add("Type files junction created.");
        }
        else
        {
            failed++;
            messages.Add("Failed to create the type files junction.");
        }

        return Result(created, removed, skipped, failed, messages);
    }

    public bool Verify(string missionPath, string targetFolder)
    {
        if (string.IsNullOrWhiteSpace(missionPath)
            || string.IsNullOrWhiteSpace(targetFolder)
            || !_fileSystem.DirectoryExists(targetFolder))
        {
            return false;
        }

        string link = MissionTypeFiles.JunctionPath(missionPath);
        if (!_junctions.IsJunction(link))
        {
            return false;
        }

        string? current = _junctions.GetTarget(link);
        return current is not null && PathsEqual(current, targetFolder);
    }

    /// <summary>
    /// Ensures the preset's type_files folder exists so the junction never points
    /// at a missing target, and so a preset with no configured files still exposes
    /// an empty folder to tools.
    /// </summary>
    private bool EnsureTarget(
        string missionPath, string targetFolder, List<string> messages, ref int failed)
    {
        if (string.IsNullOrWhiteSpace(missionPath) || string.IsNullOrWhiteSpace(targetFolder))
        {
            failed++;
            messages.Add("Cannot create the type files junction: mission or target path is empty.");
            return false;
        }

        // Never let junction creation recreate a deleted/renamed mission tree.
        // JunctionOperations.Create creates the link's parent directories, so the
        // mission folder must already exist before the link is attempted.
        if (!_fileSystem.DirectoryExists(missionPath))
        {
            failed++;
            messages.Add($"Mission folder not found: {missionPath}");
            return false;
        }

        if (_fileSystem.DirectoryExists(targetFolder))
        {
            return true;
        }

        try
        {
            _fileSystem.CreateDirectory(targetFolder);
            messages.Add($"Type files folder created: {targetFolder}");
            return true;
        }
        catch (Exception ex)
        {
            failed++;
            messages.Add($"Failed to create the type files folder: {targetFolder} ({ex.Message})");
            return false;
        }
    }

    private static JunctionSyncResult Result(
        int created, int removed, int skipped, int failed, List<string> messages) =>
        new()
        {
            Created = created,
            Removed = removed,
            Skipped = skipped,
            Failed = failed,
            Messages = messages,
        };

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a.Trim()),
            Path.TrimEndingDirectorySeparator(b.Trim()),
            StringComparison.OrdinalIgnoreCase);
}
