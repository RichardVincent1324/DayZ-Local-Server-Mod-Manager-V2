namespace DayZModManager.Core.Models;

/// <summary>
/// Metadata for a world save (<c>save-meta.json</c>). A save is only a
/// point-in-time snapshot of a DayZ world belonging to a preset: it records the
/// snapshot identity and timing, not the server/mod/types configuration (those
/// belong to the parent preset).
/// </summary>
public sealed class SaveMetaData
{
    /// <summary>The save's name (its folder name under the preset's saves folder).</summary>
    public string SaveName { get; set; } = string.Empty;

    /// <summary>When the save was first created, in UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When the world snapshot was last written, in UTC.</summary>
    public DateTime SavedAtUtc { get; set; }

    /// <summary>Leaf name of the nested storage folder (e.g. "storage_1").</summary>
    public string StorageFolder { get; set; } = string.Empty;
}
