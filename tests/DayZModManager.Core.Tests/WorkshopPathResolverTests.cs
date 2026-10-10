using DayZModManager.Core;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests;

public class WorkshopPathResolverTests
{
    [Fact]
    public void Resolve_AppendsWorkshopFolder_WhenParentSelected()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"D:\SteamLibrary\steamapps\common\DayZ", "!Workshop");

        string resolved = WorkshopPathResolver.Resolve(fs, @"D:\SteamLibrary\steamapps\common\DayZ");

        Assert.Equal(@"D:\SteamLibrary\steamapps\common\DayZ\!Workshop", resolved);
    }

    [Fact]
    public void Resolve_LeavesWorkshopFolder_WhenAlreadySelected()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"D:\SteamLibrary\steamapps\common\DayZ", "!Workshop");

        string resolved = WorkshopPathResolver.Resolve(
            fs, @"D:\SteamLibrary\steamapps\common\DayZ\!Workshop");

        Assert.Equal(@"D:\SteamLibrary\steamapps\common\DayZ\!Workshop", resolved);
    }

    [Fact]
    public void Resolve_LeavesPathUnchanged_WhenNoWorkshopChild()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"D:\mods");

        string resolved = WorkshopPathResolver.Resolve(fs, @"D:\mods");

        Assert.Equal(@"D:\mods", resolved);
    }

    [Fact]
    public void Resolve_TrimsTrailingSeparator()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"D:\mods");

        string resolved = WorkshopPathResolver.Resolve(fs, @"D:\mods\");

        Assert.Equal(@"D:\mods", resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_Empty_ReturnsEmpty(string input)
    {
        var fs = new FakeFileSystem();

        Assert.Equal(input, WorkshopPathResolver.Resolve(fs, input));
    }
}
