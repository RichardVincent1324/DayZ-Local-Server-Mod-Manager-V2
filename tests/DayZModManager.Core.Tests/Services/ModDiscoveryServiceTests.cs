using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class ModDiscoveryServiceTests
{
    [Fact]
    public void DiscoverWorkshopMods_ReturnsOnlyAtPrefixedDirectories_Sorted()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"C:\DayZ\!Workshop", "@Zen", "addons", "@CF", "@Aim", "workshop");

        var service = new ModDiscoveryService(fs);

        ModDiscoveryResult result = service.DiscoverWorkshopMods(@"C:\DayZ\!Workshop");

        Assert.True(result.PathAvailable);
        Assert.Equal(new[] { "@Aim", "@CF", "@Zen" }, result.Mods);
    }

    [Fact]
    public void DiscoverWorkshopMods_ReadableEmptyFolder_IsAvailableWithNoMods()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"C:\DayZ\!Workshop");

        ModDiscoveryResult result = new ModDiscoveryService(fs).DiscoverWorkshopMods(@"C:\DayZ\!Workshop");

        Assert.True(result.PathAvailable);
        Assert.Empty(result.Mods);
    }

    [Fact]
    public void DiscoverWorkshopMods_ReportsPathUnavailable_WhenPathMissing()
    {
        var service = new ModDiscoveryService(new FakeFileSystem());

        ModDiscoveryResult result = service.DiscoverWorkshopMods(@"C:\missing");

        Assert.False(result.PathAvailable);
        Assert.Empty(result.Mods);
    }

    [Fact]
    public void DiscoverWorkshopMods_ReportsPathUnavailable_WhenPathBlank()
    {
        var service = new ModDiscoveryService(new FakeFileSystem());

        ModDiscoveryResult result = service.DiscoverWorkshopMods(string.Empty);

        Assert.False(result.PathAvailable);
        Assert.Empty(result.Mods);
    }
}
