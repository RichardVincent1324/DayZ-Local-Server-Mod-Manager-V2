using System.Diagnostics;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// Production <see cref="IDayZServerProcessState"/> backed by the Windows process
/// table. The check is performed on demand because it is cheap.
/// </summary>
public sealed class DayZServerProcessState : IDayZServerProcessState
{
    /// <summary>Process name (without the .exe extension) of the DayZ server.</summary>
    public const string ServerProcessName = "DayZServer_x64";

    public bool IsDayZServerRunning() =>
        Process.GetProcessesByName(ServerProcessName).Length > 0;
}
