using System.Diagnostics;

namespace DayZModManager.App.Services;

/// <summary>Starts external processes (the server launch batch file) and opens folders.</summary>
public interface IProcessLauncher
{
    void Launch(string filePath, string workingDirectory);

    /// <summary>Opens the given folder in the Windows File Explorer.</summary>
    void OpenFolder(string path);
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

    public void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}
