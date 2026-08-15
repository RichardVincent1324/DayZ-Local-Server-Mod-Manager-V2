using System.Diagnostics;

namespace DayZModManager.App.Services;

/// <summary>Starts external processes (the server launch batch file).</summary>
public interface IProcessLauncher
{
    void Launch(string filePath, string workingDirectory);
}

public sealed class ProcessLauncher : IProcessLauncher
{
    public void Launch(string filePath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = filePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        };

        Process.Start(startInfo);
    }
}
