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

    [Fact]
    public void IsValidWorkshopPath_TrueForWorkshopFolderByName()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"D:\SteamLibrary\steamapps\common\DayZ\!Workshop", "@CF");

        Assert.True(WorkshopPathResolver.IsValidWorkshopPath(
            fs, @"D:\SteamLibrary\steamapps\common\DayZ\!Workshop"));
    }

    [Fact]
    public void IsValidWorkshopPath_TrueForEmptyWorkshopFolder()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"D:\SteamLibrary\steamapps\common\DayZ\!Workshop");

        Assert.True(WorkshopPathResolver.IsValidWorkshopPath(
            fs, @"D:\SteamLibrary\steamapps\common\DayZ\!Workshop"));
    }

    [Fact]
    public void IsValidWorkshopPath_TrueForDayZGameFolder()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"D:\SteamLibrary\steamapps\common\DayZ");

        Assert.True(WorkshopPathResolver.IsValidWorkshopPath(
            fs, @"D:\SteamLibrary\steamapps\common\DayZ"));
    }

    [Fact]
    public void IsValidWorkshopPath_TrueForCustomFolderWithDirectModFolders()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"D:\DayZMods", "@CF", "@Expansion");

        Assert.True(WorkshopPathResolver.IsValidWorkshopPath(fs, @"D:\DayZMods"));
    }

    [Fact]
    public void IsValidWorkshopPath_FalseWhenModFoldersAreNestedDeeper()
    {
        // The @mod folders live under DayZ\!Workshop, not directly under "common".
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"D:\SteamLibrary\steamapps\common", "DayZ");
        fs.AddDirectory(@"D:\SteamLibrary\steamapps\common\DayZ", "!Workshop");
        fs.AddDirectory(@"D:\SteamLibrary\steamapps\common\DayZ\!Workshop", "@CF");

        Assert.False(WorkshopPathResolver.IsValidWorkshopPath(
            fs, @"D:\SteamLibrary\steamapps\common"));
    }

    [Fact]
    public void IsValidWorkshopPath_FalseForUnrelatedFolder()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"D:\random", "stuff");

        Assert.False(WorkshopPathResolver.IsValidWorkshopPath(fs, @"D:\random"));
    }

    [Fact]
    public void IsValidWorkshopPath_FalseWhenDirectoryMissing()
    {
        var fs = new FakeFileSystem();

        Assert.False(WorkshopPathResolver.IsValidWorkshopPath(fs, @"D:\missing"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidWorkshopPath_FalseForEmpty(string input)
    {
        var fs = new FakeFileSystem();

        Assert.False(WorkshopPathResolver.IsValidWorkshopPath(fs, input));
    }
}
