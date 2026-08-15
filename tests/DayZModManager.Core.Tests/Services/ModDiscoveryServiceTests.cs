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

        IReadOnlyList<string> result = service.DiscoverWorkshopMods(@"C:\DayZ\!Workshop");

        Assert.Equal(new[] { "@Aim", "@CF", "@Zen" }, result);
    }

    [Fact]
    public void DiscoverWorkshopMods_ReturnsEmpty_WhenPathMissing()
    {
        var service = new ModDiscoveryService(new FakeFileSystem());

        Assert.Empty(service.DiscoverWorkshopMods(@"C:\missing"));
    }

    [Fact]
    public void DiscoverWorkshopMods_ReturnsEmpty_WhenPathBlank()
    {
        var service = new ModDiscoveryService(new FakeFileSystem());

        Assert.Empty(service.DiscoverWorkshopMods(string.Empty));
    }
}
