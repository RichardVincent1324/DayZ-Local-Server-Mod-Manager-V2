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
    /// The preset's dedicated DayZ instance ID. The value in the preset's
    /// <c>serverDZ.cfg</c> is authoritative at runtime (that is what DayZ reads to
    /// locate the live storage folder); this is the app-managed record of it.
    /// Defaults to 0 so a missing field is treated as "unknown" and the config is
    /// used instead of silently claiming instance ID 1.
    /// </summary>
    public int InstanceId { get; set; }

    /// <summary>When the preset was created, in UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
