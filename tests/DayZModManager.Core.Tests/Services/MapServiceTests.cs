using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class MapServiceTests
{
    private const string ServerPath = @"D:\DayZServer";
    private const string MissionsPath = @"D:\DayZServer\mpmissions";

    [Fact]
    public void DiscoverMaps_ReturnsOnlyFoldersWithEconomyCore_Sorted()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(MissionsPath, "dayzOffline.sakhal", "dayzOffline.chernarusplus", "noEconomy");
        fs.AddFile($@"{MissionsPath}\dayzOffline.chernarusplus\cfgeconomycore.xml", "<economycore/>");
        fs.AddFile($@"{MissionsPath}\dayzOffline.sakhal\cfgeconomycore.xml", "<economycore/>");

        IReadOnlyList<Core.Models.MapInfo> maps = new MapService(fs).DiscoverMaps(ServerPath);

        Assert.Equal(2, maps.Count);
        Assert.Equal("dayzOffline.chernarusplus", maps[0].Name);
        Assert.Equal("dayzOffline.sakhal", maps[1].Name);
    }

    [Fact]
    public void ResolveMapPath_ReturnsFullPath()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(MissionsPath, "dayzOffline.sakhal");
        fs.AddFile($@"{MissionsPath}\dayzOffline.sakhal\cfgeconomycore.xml", "<economycore/>");

        string? path = new MapService(fs).ResolveMapPath(ServerPath, "dayzOffline.sakhal");

        Assert.Equal($@"{MissionsPath}\dayzOffline.sakhal", path);
    }

    [Fact]
    public void ResolveMapPath_ReturnsNull_WhenNotFound()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(MissionsPath, "dayzOffline.sakhal");

        Assert.Null(new MapService(fs).ResolveMapPath(ServerPath, "missing.map"));
    }
}
