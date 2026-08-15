using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class BatchFileServiceTests
{
    private const string BatPath = @"D:\DayZServer\LocalServer.example.bat";

    [Fact]
    public void ReadModList_ParsesNamesWithSpacesAndTrailingSemicolon()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, """
            @echo off
            set "serverName=LocalServer"
            set "modList=-mod=@CF;@Dabs Framework;@VPPAdminTools;"
            """);
        var service = new BatchFileService(fs);

        IReadOnlyList<string> result = service.ReadModList(BatPath);

        Assert.Equal(new[] { "@CF", "@Dabs Framework", "@VPPAdminTools" }, result);
    }

    [Fact]
    public void ReadModList_ReturnsEmpty_WhenModListIsEmpty()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"modList=-mod=;\"");
        var service = new BatchFileService(fs);

        Assert.Empty(service.ReadModList(BatPath));
    }

    [Fact]
    public void ReadModList_ReturnsEmpty_WhenNoModListLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "@echo off\nset \"serverName=LocalServer\"");
        var service = new BatchFileService(fs);

        Assert.Empty(service.ReadModList(BatPath));
    }

    [Fact]
    public void ReadModList_ReturnsEmpty_WhenFileMissing()
    {
        var service = new BatchFileService(new FakeFileSystem());

        Assert.Empty(service.ReadModList(BatPath));
    }

    [Fact]
    public void WriteModList_ReplacesOnlyModListLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"serverName=LocalServer\"\nset \"modList=-mod=@CF;@Old;\"\nset \"serverPort=2302\"");
        var service = new BatchFileService(fs);

        bool result = service.WriteModList(BatPath, new[] { "@CF", "@New Mod" });

        Assert.True(result);
        string content = fs.TryGetFileContents(BatPath)!;
        Assert.Contains("set \"serverName=LocalServer\"", content);
        Assert.Contains("set \"modList=-mod=@CF;@New Mod;\"", content);
        Assert.Contains("set \"serverPort=2302\"", content);
        Assert.DoesNotContain("@Old", content);
    }

    [Fact]
    public void WriteModList_ReturnsFalse_WhenNoModListLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"serverName=LocalServer\"");
        var service = new BatchFileService(fs);

        Assert.False(service.WriteModList(BatPath, new[] { "@CF" }));
    }

    [Fact]
    public void WriteModList_PreservesCrlfLineEndings()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"modList=-mod=@Old;\"\r\nset \"serverName=X\"\r\n");
        var service = new BatchFileService(fs);

        service.WriteModList(BatPath, new[] { "@CF" });

        string content = fs.TryGetFileContents(BatPath)!;
        Assert.Contains("set \"modList=-mod=@CF;\"\r\n", content);
        Assert.DoesNotContain("@Old", content);
        Assert.EndsWith("\r\n", content);
    }

    [Fact]
    public void WriteModList_WritesEmptyModList_WhenNoMods()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"modList=-mod=@Old;\"");
        var service = new BatchFileService(fs);

        service.WriteModList(BatPath, Array.Empty<string>());

        Assert.Contains("set \"modList=-mod=\"", fs.TryGetFileContents(BatPath)!);
    }

    [Fact]
    public void WriteServerProfile_ReplacesOnlyProfileLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"serverProfile=map_profiles\\chernarusplus\"\nset \"serverName=X\"");
        var service = new BatchFileService(fs);

        Assert.True(service.WriteServerProfile(BatPath, "map_profiles\\sakhal"));
        Assert.Contains("set \"serverProfile=map_profiles\\sakhal\"", fs.TryGetFileContents(BatPath)!);
        Assert.Contains("set \"serverName=X\"", fs.TryGetFileContents(BatPath)!);
    }
}
