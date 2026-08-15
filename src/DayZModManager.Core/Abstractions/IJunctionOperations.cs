namespace DayZModManager.Core.Abstractions;

/// <summary>
/// Low-level NTFS junction operations. Isolated behind an interface so the
/// higher-level <c>JunctionService</c> can be unit tested without touching the
/// real filesystem or requiring elevation.
/// </summary>
public interface IJunctionOperations
{
    /// <summary>Returns true if <paramref name="path"/> exists and is a directory junction (mount point).</summary>
    bool IsJunction(string path);

    /// <summary>
    /// Creates a junction at <paramref name="linkPath"/> pointing to
    /// <paramref name="targetPath"/>. Returns false on failure.
    /// </summary>
    bool Create(string linkPath, string targetPath);

    /// <summary>
    /// Removes the junction at <paramref name="linkPath"/>. Returns false only if
    /// the path exists and is not a junction (never deletes a physical directory).
    /// </summary>
    bool Delete(string linkPath);
}
