using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Tests.TestDoubles;

/// <summary>In-memory <see cref="IJunctionOperations"/> for unit tests.</summary>
public sealed class FakeJunctionOperations : IJunctionOperations
{
    private readonly HashSet<string> _junctions = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FailCreate { get; set; }

    public bool FailDelete { get; set; }

    public bool IsJunction(string path) => _junctions.Contains(path);

    public bool Create(string linkPath, string targetPath)
    {
        if (FailCreate)
        {
            return false;
        }

        _junctions.Add(linkPath);
        Targets[linkPath] = targetPath;
        return true;
    }

    public bool Delete(string linkPath)
    {
        if (FailDelete)
        {
            return false;
        }

        if (!_junctions.Contains(linkPath))
        {
            return true;
        }

        _junctions.Remove(linkPath);
        Targets.Remove(linkPath);
        return true;
    }
}
