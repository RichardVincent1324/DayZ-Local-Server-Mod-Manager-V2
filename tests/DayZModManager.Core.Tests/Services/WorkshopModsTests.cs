using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class WorkshopModsTests
{
    private const string Workshop = @"D:\DayZ\!Workshop";

    [Fact]
    public void FindPresent_ReturnsAtPrefixedDirectoriesOnly()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(Workshop, "@Zen", "addons", "@CF");

        Assert.Equal(new[] { "@Zen", "@CF" }, WorkshopMods.FindPresent(fs, Workshop));
    }

    [Fact]
    public void FindMissing_KeepsLoadOrder()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(Workshop, "@CF");

        IReadOnlyList<string> missing = WorkshopMods.FindMissing(
            fs, Workshop, new[] { "@GhostB", "@CF", "@GhostA" });

        Assert.Equal(new[] { "@GhostB", "@GhostA" }, missing);
    }

    [Fact]
    public void FindMissing_IsCaseInsensitive()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(Workshop, "@CF");

        Assert.Empty(WorkshopMods.FindMissing(fs, Workshop, new[] { "@cf" }));
    }

    [Fact]
    public void FindMissing_ReadableEmptyFolder_ReportsEveryLoadedMod()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(Workshop);

        IReadOnlyList<string> missing = WorkshopMods.FindMissing(fs, Workshop, new[] { "@CF" });

        Assert.Equal(new[] { "@CF" }, missing);
    }

    [Fact]
    public void FindMissing_UnusablePath_ReportsNothing()
    {
        var fs = new FakeFileSystem();

        Assert.Empty(WorkshopMods.FindMissing(fs, @"D:\missing", new[] { "@CF" }));
        Assert.Empty(WorkshopMods.FindMissing(fs, string.Empty, new[] { "@CF" }));
    }
}
