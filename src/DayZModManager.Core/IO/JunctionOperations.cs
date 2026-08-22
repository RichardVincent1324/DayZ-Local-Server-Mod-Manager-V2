using System.Runtime.InteropServices;
using System.Text;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.IO;

/// <summary>
/// Production <see cref="IJunctionOperations"/>. Junction creation, detection
/// and removal all use Win32 APIs directly (no <c>mklink</c> shell out and no
/// elevation), so a junction is never confused with a physical directory or a
/// symlink and paths with spaces or special characters are handled correctly.
/// </summary>
public sealed class JunctionOperations : IJunctionOperations
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWriteDelete = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FsctlGetReparsePoint = 0x000900A8;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint IoReparseTagMountPoint = 0xA0000003;
    private const int MaxReparseBufferSize = 16384;
    private static readonly IntPtr InvalidHandle = new(-1);

    public bool IsJunction(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // Detection opens the path with FILE_FLAG_OPEN_REPARSE_POINT (see
        // ReadReparsePoint), so a junction is detected even when its target is
        // missing ("broken" junction). Directory.Exists is deliberately not used
        // here: it follows reparse points and reports false for broken junctions.
        return ReadReparsePoint(path).Tag == IoReparseTagMountPoint;
    }

    public string? GetTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        (uint tag, string? substituteName) = ReadReparsePoint(path);
        return tag == IoReparseTagMountPoint ? substituteName : null;
    }

    public bool Create(string linkPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(linkPath) || string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        // Already a junction (valid or broken): leave it alone. The service
        // decides whether to re-target via GetTarget/Delete before calling Create.
        if (IsJunction(linkPath))
        {
            return true;
        }

        // A physical directory here is a conflict; never turn it into a junction.
        if (Directory.Exists(linkPath))
        {
            return false;
        }

        if (!Directory.Exists(targetPath))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(linkPath);
        }
        catch
        {
            return false;
        }

        IntPtr handle = CreateFile(
            linkPath,
            GenericRead | GenericWrite,
            FileShareReadWriteDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);

        if (handle == InvalidHandle)
        {
            TryDeleteEmptyDirectory(linkPath);
            return false;
        }

        try
        {
            byte[] data = BuildMountPointReparseData(targetPath);
            IntPtr buffer = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, buffer, data.Length);

                if (!DeviceIoControl(
                        handle,
                        FsctlSetReparsePoint,
                        buffer,
                        (uint)data.Length,
                        IntPtr.Zero,
                        0,
                        out uint _,
                        IntPtr.Zero))
                {
                    TryDeleteEmptyDirectory(linkPath);
                    return false;
                }
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

        return IsJunction(linkPath);
    }

    public bool Delete(string linkPath)
    {
        if (string.IsNullOrWhiteSpace(linkPath))
        {
            return true;
        }

        // Detects junctions even when the target is gone (broken junction), so a
        // stale reparse point can actually be removed.
        if (IsJunction(linkPath))
        {
            return RemoveDirectory(linkPath);
        }

        // Not a junction: only true when nothing exists. Never delete a physical
        // directory or file.
        return !Directory.Exists(linkPath);
    }

    /// <summary>
    /// Builds a <c>REPARSE_DATA_BUFFER</c> describing a mount-point (junction)
    /// reparse point. The substitute name is the NT-namespace absolute path
    /// (<c>\??\C:\...</c>) and the print name is the friendly target path.
    /// </summary>
    internal static byte[] BuildMountPointReparseData(string targetPath)
    {
        // NT-namespace form: \??\C:\... for drive paths, \??\UNC\server\share\...
        // for UNC (network) paths.
        string substituteName = targetPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\??\UNC" + targetPath[1..]
            : @"\??\" + targetPath;
        byte[] substituteBytes = Encoding.Unicode.GetBytes(substituteName);
        byte[] printBytes = Encoding.Unicode.GetBytes(targetPath);

        // MountPointReparseBuffer union header: SubstituteNameOffset(2) +
        // SubstituteNameLength(2) + PrintNameOffset(2) + PrintNameLength(2) = 8
        // bytes before PathBuffer. Note there is no Flags field (that only exists
        // in the SymbolicLinkReparseBuffer variant). Both names must be
        // null-terminated in the buffer and those nulls count toward the length.
        const int mountPointHeaderSize = 8;
        const int nullTerminatorSize = sizeof(ushort);
        int reparseDataLength = mountPointHeaderSize + substituteBytes.Length + nullTerminatorSize + printBytes.Length + nullTerminatorSize;

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true))
        {
            writer.Write(IoReparseTagMountPoint);
            writer.Write((ushort)reparseDataLength);
            writer.Write((ushort)0);
            writer.Write((ushort)0); // SubstituteNameOffset
            writer.Write((ushort)substituteBytes.Length); // SubstituteNameLength
            writer.Write((ushort)(substituteBytes.Length + nullTerminatorSize)); // PrintNameOffset
            writer.Write((ushort)printBytes.Length); // PrintNameLength
            writer.Write(substituteBytes);
            writer.Write((ushort)0); // Substitute-name null terminator
            writer.Write(printBytes);
            writer.Write((ushort)0); // Print-name null terminator
        }

        return stream.ToArray();
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path);
            }
        }
        catch
        {
            // Best effort: leave the empty directory if cleanup fails.
        }
    }

    internal static string CleanTarget(string substituteName)
    {
        string cleaned = substituteName.Trim();

        if (cleaned.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            cleaned = cleaned[4..];
        }
        else if (cleaned.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            cleaned = cleaned[4..];
        }

        // \??\UNC\server\share -> \\server\share
        if (cleaned.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + cleaned[4..];
        }

        return cleaned;
    }

    private static (uint Tag, string? SubstituteName) ReadReparsePoint(string path)
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
            return (0, null);
        }

        try
        {
            IntPtr buffer = Marshal.AllocHGlobal(MaxReparseBufferSize);
            try
            {
                if (!DeviceIoControl(
                        handle,
                        FsctlGetReparsePoint,
                        IntPtr.Zero,
                        0,
                        buffer,
                        (uint)MaxReparseBufferSize,
                        out uint _,
                        IntPtr.Zero))
                {
                    return (0, null);
                }

                uint tag = (uint)Marshal.ReadInt32(buffer);
                if (tag != IoReparseTagMountPoint)
                {
                    return (tag, null);
                }

                // REPARSE_DATA_BUFFER fixed header is 8 bytes; MountPointReparseBuffer
                // adds 8 more before the PathBuffer (offset 16). The name offsets are
                // relative to PathBuffer, so the absolute offset is 16 + offset.
                const int mountPointHeaderSize = 8;
                ushort substituteNameOffset = (ushort)Marshal.ReadInt16(buffer, 8 + 0);
                ushort substituteNameLength = (ushort)Marshal.ReadInt16(buffer, 8 + 2);
                if (substituteNameLength == 0)
                {
                    return (tag, null);
                }

                string substituteName = Marshal.PtrToStringUni(
                    IntPtr.Add(buffer, 8 + mountPointHeaderSize + substituteNameOffset),
                    substituteNameLength / 2)!;

                return (tag, CleanTarget(substituteName));
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
