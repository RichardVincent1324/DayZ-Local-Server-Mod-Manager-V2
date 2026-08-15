using System.Diagnostics;
using System.Runtime.InteropServices;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.IO;

/// <summary>
/// Production <see cref="IJunctionOperations"/>. Junction creation uses
/// <c>mklink /J</c> (no elevation required), while detection and removal use
/// Win32 APIs so a junction is never confused with a physical directory or a
/// symlink.
/// </summary>
    public sealed class JunctionOperations : IJunctionOperations
{
    private const int CreateTimeoutMilliseconds = 30000;

    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareReadWriteDelete = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FsctlGetReparsePoint = 0x000900A8;
    private const uint IoReparseTagMountPoint = 0xA0000003;
    private const int MaxReparseBufferSize = 16384;
    private static readonly IntPtr InvalidHandle = new(-1);

    public bool IsJunction(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        uint attributes = GetFileAttributes(path);
        if (attributes == uint.MaxValue || (attributes & FileAttributeReparsePoint) == 0)
        {
            return false;
        }

        return GetReparseTag(path) == IoReparseTagMountPoint;
    }

    public bool Create(string linkPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(linkPath) || string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        if (Directory.Exists(linkPath))
        {
            return IsJunction(linkPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        if (!process.WaitForExit(CreateTimeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort; the process may already have exited.
            }

            return false;
        }

        return process.ExitCode == 0 && IsJunction(linkPath);
    }

    public bool Delete(string linkPath)
    {
        if (string.IsNullOrWhiteSpace(linkPath))
        {
            return true;
        }

        if (!Directory.Exists(linkPath))
        {
            return true;
        }

        if (!IsJunction(linkPath))
        {
            return false;
        }

        return RemoveDirectory(linkPath);
    }

    private static uint GetReparseTag(string path)
    {
        IntPtr handle = CreateFile(
            path,
            GenericRead,
            FileShareReadWriteDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);

        if (handle == InvalidHandle)
        {
            return 0;
        }

        try
        {
            IntPtr buffer = Marshal.AllocHGlobal(MaxReparseBufferSize);
            try
            {
                if (DeviceIoControl(
                        handle,
                        FsctlGetReparsePoint,
                        IntPtr.Zero,
                        0,
                        buffer,
                        (uint)MaxReparseBufferSize,
                        out uint _,
                        IntPtr.Zero))
                {
                    return (uint)Marshal.ReadInt32(buffer);
                }

                return 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileAttributes(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool RemoveDirectory(string lpPathName);
}
