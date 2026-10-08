namespace DayZModManager.Core.Models;

/// <summary>
/// Persistent metadata for a preset (<c>preset-meta.json</c>). A preset owns a
/// dedicated, permanent <see cref="InstanceId"/> that identifies its runtime
/// world storage slot (<c>mpmissions\&lt;map&gt;\storage_&lt;instanceId&gt;</c>).
/// The instance ID belongs to the preset, never to an individual save.
/// </summary>
public sealed class PresetMetaData
{
    /// <summary>
    /// The preset's dedicated DayZ instance ID. Read from the preset's
    /// <c>serverDZ.cfg</c> at runtime to locate the live storage folder.
    /// </summary>
    public int InstanceId { get; set; } = 1;

    /// <summary>When the preset was created, in UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
