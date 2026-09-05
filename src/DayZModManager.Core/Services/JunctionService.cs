using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>Result of synchronizing server-side junctions with the loaded mod set.</summary>
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
/// server root with the desired loaded-mod set. Loaded mods get a junction; any
/// existing junction that is not loaded is removed (which also cleans up orphans
/// whose source mod was deleted). Junctions created directly in the server root
/// by older versions are left untouched.
/// </summary>
public interface IJunctionService
{
    JunctionSyncResult Sync(string serverPath, string workshopPath, IReadOnlyList<string> loadedMods);

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
        var messages = new List<string>();
        int created = 0, removed = 0, skipped = 0, failed = 0;
        var loadedSet = new HashSet<string>(loadedMods, StringComparer.OrdinalIgnoreCase);

        string junctionDir = Path.Combine(serverPath, ModListFolder.Name);
        if (!_fileSystem.DirectoryExists(junctionDir))
        {
            try
            {
                _fileSystem.CreateDirectory(junctionDir);
            }
            catch (Exception ex)
            {
                failed++;
                messages.Add($"Failed to create mod folder: {junctionDir} ({ex.Message})");
                return new JunctionSyncResult
                {
                    Created = created,
                    Removed = removed,
                    Skipped = skipped,
                    Failed = failed,
                    Messages = messages,
                };
            }

            messages.Add($"Mod folder created: {ModListFolder.Name}");
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

                // The junction already exists but points somewhere else (e.g. the
                // workshop path changed). Remove it and fall through to recreate it
                // against the current target.
                if (!_junctions.Delete(link))
                {
                    failed++;
                    messages.Add($"Failed to remove stale junction: {mod}");
                    continue;
                }

                messages.Add($"Junction re-targeted: {mod}");
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
            Skipped = skipped,
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
