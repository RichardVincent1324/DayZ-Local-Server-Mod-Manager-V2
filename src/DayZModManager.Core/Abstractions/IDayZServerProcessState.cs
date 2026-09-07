namespace DayZModManager.Core.Abstractions;

/// <summary>Reports whether the DayZ server process is currently running.</summary>
public interface IDayZServerProcessState
{
    /// <summary>
    /// True when a <c>DayZServer_x64</c> process is running. Used to refuse
    /// operations that would touch the server's live storage folder while the
    /// server may be writing to it.
    /// </summary>
    bool IsDayZServerRunning();
}
