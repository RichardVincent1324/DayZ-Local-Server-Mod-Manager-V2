using System.Text;
using DayZModManager.Core.IO;

namespace DayZModManager.Core.Tests.Services;

public class JunctionOperationsTests
{
    [Fact]
    public void BuildMountPointReparseData_ProducesWellFormedMountPointBuffer()
    {
        const string target = @"C:\Steam\Workshop\@CF";
        byte[] data = JunctionOperations.BuildMountPointReparseData(target);

        string substituteName = @"\??\" + target;
        byte[] substituteBytes = Encoding.Unicode.GetBytes(substituteName);
        byte[] printBytes = Encoding.Unicode.GetBytes(target);

        const int fixedHeaderSize = 8; // tag + reparse data length + reserved
        const int mountPointHeaderSize = 8; // 4 USHORTs before PathBuffer
        const int nullTerminatorSize = sizeof(ushort);

        Assert.Equal(
            fixedHeaderSize + mountPointHeaderSize + substituteBytes.Length + nullTerminatorSize + printBytes.Length + nullTerminatorSize,
            data.Length);

        // Fixed header.
        Assert.Equal(0xA0000003, BitConverter.ToUInt32(data, 0)); // IO_REPARSE_TAG_MOUNT_POINT
        Assert.Equal(
            (ushort)(mountPointHeaderSize + substituteBytes.Length + nullTerminatorSize + printBytes.Length + nullTerminatorSize),
            BitConverter.ToUInt16(data, 4));
        Assert.Equal((ushort)0, BitConverter.ToUInt16(data, 6)); // Reserved

        // Mount point union header (no Flags member for mount points).
        Assert.Equal((ushort)0, BitConverter.ToUInt16(data, 8)); // SubstituteNameOffset
        Assert.Equal((ushort)substituteBytes.Length, BitConverter.ToUInt16(data, 10)); // SubstituteNameLength
        Assert.Equal((ushort)(substituteBytes.Length + nullTerminatorSize), BitConverter.ToUInt16(data, 12)); // PrintNameOffset
        Assert.Equal((ushort)printBytes.Length, BitConverter.ToUInt16(data, 14)); // PrintNameLength

        // PathBuffer starts at offset 16: substitute name, null terminator,
        // print name, null terminator.
        int pathBuffer = fixedHeaderSize + mountPointHeaderSize;
        Assert.Equal(substituteName, Encoding.Unicode.GetString(data, pathBuffer, substituteBytes.Length));
        Assert.Equal((ushort)0, BitConverter.ToUInt16(data, pathBuffer + substituteBytes.Length));
        Assert.Equal(
            target,
            Encoding.Unicode.GetString(data, pathBuffer + substituteBytes.Length + nullTerminatorSize, printBytes.Length));
        Assert.Equal(
            (ushort)0,
            BitConverter.ToUInt16(data, pathBuffer + substituteBytes.Length + nullTerminatorSize + printBytes.Length));
    }

    [Fact]
    public void BuildMountPointReparseData_UsesUncSubstituteName_ForNetworkPath()
    {
        const string target = @"\\server\share\@CF";
        byte[] data = JunctionOperations.BuildMountPointReparseData(target);

        ushort substituteNameLength = BitConverter.ToUInt16(data, 10);
        const int pathBuffer = 16;
        string substitute = Encoding.Unicode.GetString(data, pathBuffer, substituteNameLength);

        Assert.Equal(@"\??\UNC\server\share\@CF", substitute);
    }

    [Theory]
    [InlineData(@"\??\C:\Steam\@CF", @"C:\Steam\@CF")]
    [InlineData(@"\??\UNC\server\share\@CF", @"\\server\share\@CF")]
    [InlineData(@"\\?\UNC\server\share\@CF", @"\\server\share\@CF")]
    [InlineData(@"\??\D:\DayZ\!Workshop\@CF", @"D:\DayZ\!Workshop\@CF")]
    public void CleanTarget_NormalizesSubstituteNames(string substituteName, string expected)
    {
        Assert.Equal(expected, JunctionOperations.CleanTarget(substituteName));
    }

    [Fact]
    public void Create_IsJunction_GetTarget_Delete_RoundTripsOnRealFilesystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Junctions (NTFS mount points) are Windows-specific.
        }

        var ops = new JunctionOperations();
        string root = Path.Combine(Path.GetTempPath(), "DZMM-Junction-Tests-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "target");
        string link = Path.Combine(root, "link");

        try
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "marker.txt"), "x");

            Assert.True(ops.Create(link, target), "Create should succeed");
            Assert.True(ops.IsJunction(link));
            Assert.Equal(target, ops.GetTarget(link));
            Assert.True(File.Exists(Path.Combine(link, "marker.txt")), "junction should resolve to the target directory");

            Assert.True(ops.Delete(link));
            Assert.False(ops.IsJunction(link));
            Assert.False(Directory.Exists(link));
            Assert.True(File.Exists(Path.Combine(target, "marker.txt")), "deleting the junction must not touch the target");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch
                {
                    // Best effort cleanup.
                }
            }
        }
    }

    [Fact]
    public void BrokenJunction_IsDetected_CanBeDeleted_AndPathReused()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Junctions (NTFS mount points) are Windows-specific.
        }

        var ops = new JunctionOperations();
        string root = Path.Combine(Path.GetTempPath(), "DZMM-Junction-Tests-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "target");
        string link = Path.Combine(root, "link");

        try
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "marker.txt"), "x");

            Assert.True(ops.Create(link, target));

            // Break the junction by removing its target, as happens when a mod is
            // uninstalled from the workshop while its junction still exists.
            Directory.Delete(target, recursive: true);

            // Detection must still work on a broken junction.
            Assert.True(ops.IsJunction(link), "broken junction should still be detected");
            Assert.Equal(target, ops.GetTarget(link));

            // Delete must remove the reparse point even when the target is gone.
            Assert.True(ops.Delete(link));
            Assert.False(ops.IsJunction(link));
            Assert.False(Directory.Exists(link));

            // The same link path can then be reused for a fresh junction.
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "marker.txt"), "y");
            Assert.True(ops.Create(link, target));
            Assert.True(ops.IsJunction(link));
            Assert.Equal(target, ops.GetTarget(link));
        }
        finally
        {
            // Remove the junction explicitly so the recursive cleanup below never
            // follows a junction into its (now deleted) target.
            try
            {
                ops.Delete(link);
            }
            catch
            {
                // Best effort cleanup.
            }

            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch
                {
                    // Best effort cleanup.
                }
            }
        }
    }
}
