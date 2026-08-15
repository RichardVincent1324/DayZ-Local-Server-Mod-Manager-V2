namespace DayZModManager.Core.Models;

/// <summary>
/// The core mod state model.
///
/// <see cref="LoadedMods"/> is the single source of truth for the active mod
/// configuration: a mod's presence in the list — and only that — defines
/// whether it is loaded. There is no separate "IsChecked"/"IsEnabled" flag.
///
/// <see cref="WorkshopMods"/> is a snapshot of the mods currently present in
/// the Steam Workshop directory. <see cref="AvailableMods"/> and
/// <see cref="MissingMods"/> are always derived from the first two and cached
/// until a mutation occurs.
/// </summary>
public sealed class ModState
{
    private readonly List<string> _workshopMods = new();
    private readonly List<string> _loadedMods = new();
    private List<string> _availableMods = new();
    private List<string> _missingMods = new();

    /// <summary>Ordered list of currently selected mods. This is the mod load order.</summary>
    public IReadOnlyList<string> LoadedMods => _loadedMods;

    /// <summary>Snapshot of all valid mods discovered in the Workshop directory.</summary>
    public IReadOnlyList<string> WorkshopMods => _workshopMods;

    /// <summary>Workshop mods that are not loaded, sorted by name.</summary>
    public IReadOnlyList<string> AvailableMods => _availableMods;

    /// <summary>Loaded mods that are no longer present in the Workshop directory.</summary>
    public IReadOnlyList<string> MissingMods => _missingMods;

    /// <summary>
    /// Replaces the Workshop snapshot. Previously loaded mods that are no longer
    /// present become visible through <see cref="MissingMods"/> but are left in
    /// <see cref="LoadedMods"/> so the user can decide what to do with them.
    /// </summary>
    public void SetWorkshopMods(IEnumerable<string> mods)
    {
        _workshopMods.Clear();
        _workshopMods.AddRange(Normalize(mods));
        RecomputeDerived();
    }

    /// <summary>
    /// Replaces the loaded-mod list wholesale. Used to initialize state from a
    /// persisted configuration (e.g. mod_order.json).
    /// </summary>
    public void ReplaceLoadedMods(IEnumerable<string> mods)
    {
        _loadedMods.Clear();
        _loadedMods.AddRange(Normalize(mods));
        RecomputeDerived();
    }

    /// <summary>
    /// Moves a mod from the available set into the loaded list (appended at the
    /// end). Succeeds only for a mod that exists in the Workshop and is not
    /// already loaded.
    /// </summary>
    public bool Load(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName)) return false;
        if (ContainsIgnoreCase(_loadedMods, modName)) return false;
        if (!ContainsIgnoreCase(_workshopMods, modName)) return false;

        _loadedMods.Add(modName);
        RecomputeDerived();
        return true;
    }

    /// <summary>Removes a mod from the loaded list. Returns true if it was removed.</summary>
    public bool Unload(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName)) return false;

        int index = IndexOfIgnoreCase(modName);
        if (index < 0) return false;

        _loadedMods.RemoveAt(index);
        RecomputeDerived();
        return true;
    }

    /// <summary>Moves a loaded mod one position toward the start of the load order.</summary>
    public bool MoveUp(string modName)
    {
        int index = IndexOfIgnoreCase(modName);
        if (index <= 0) return false;

        (_loadedMods[index - 1], _loadedMods[index]) = (_loadedMods[index], _loadedMods[index - 1]);
        return true;
    }

    /// <summary>Moves a loaded mod one position toward the end of the load order.</summary>
    public bool MoveDown(string modName)
    {
        int index = IndexOfIgnoreCase(modName);
        if (index < 0 || index >= _loadedMods.Count - 1) return false;

        (_loadedMods[index + 1], _loadedMods[index]) = (_loadedMods[index], _loadedMods[index + 1]);
        return true;
    }

    /// <summary>
    /// Moves a loaded mod to the given zero-based final index (clamped to the
    /// valid range). Returns false if the mod is not loaded or its position is
    /// unchanged.
    /// </summary>
    public bool Move(string modName, int newIndex)
    {
        int oldIndex = IndexOfIgnoreCase(modName);
        if (oldIndex < 0) return false;

        if (newIndex < 0) newIndex = 0;
        if (newIndex > _loadedMods.Count - 1) newIndex = _loadedMods.Count - 1;
        if (oldIndex == newIndex) return false;

        _loadedMods.RemoveAt(oldIndex);
        _loadedMods.Insert(newIndex, modName);
        return true;
    }

    private void RecomputeDerived()
    {
        var workshopSet = new HashSet<string>(_workshopMods, StringComparer.OrdinalIgnoreCase);
        var loadedSet = new HashSet<string>(_loadedMods, StringComparer.OrdinalIgnoreCase);

        _availableMods = _workshopMods
            .Where(mod => !loadedSet.Contains(mod))
            .OrderBy(mod => mod, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _missingMods = _loadedMods
            .Where(mod => !workshopSet.Contains(mod))
            .ToList();
    }

    private static IEnumerable<string> Normalize(IEnumerable<string> mods) =>
        mods
            .Where(mod => !string.IsNullOrWhiteSpace(mod))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool ContainsIgnoreCase(IEnumerable<string> values, string value) =>
        values.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));

    private int IndexOfIgnoreCase(string value) =>
        _loadedMods.FindIndex(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
}
