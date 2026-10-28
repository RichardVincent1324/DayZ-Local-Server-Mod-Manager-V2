namespace DayZModManager.App.ViewModels;

/// <summary>
/// A row in the Preset &amp; Types "Progress Saves" list. It is either a stored save
/// snapshot (<see cref="IsOrphaned"/> false) or a live <c>storage_&lt;id&gt;</c>
/// folder that no preset owns (orphan/unattached storage).
/// </summary>
public sealed class SaveListEntryViewModel : ViewModelBase
{
    public SaveListEntryViewModel(string name, bool isOrphaned, int? instanceId = null)
    {
        Name = name;
        IsOrphaned = isOrphaned;
        InstanceId = instanceId;
    }

    /// <summary>The stored save's name, or the orphan storage folder leaf (e.g. "storage_5").</summary>
    public string Name { get; }

    /// <summary>True for unattached live storage that no preset owns.</summary>
    public bool IsOrphaned { get; }

    /// <summary>The orphan storage folder's instance ID; null for stored saves.</summary>
    public int? InstanceId { get; }

    /// <summary>User-facing label; unattached storage is flagged as orphaned.</summary>
    public string DisplayName => IsOrphaned ? $"{Name} (orphaned)" : Name;
}
