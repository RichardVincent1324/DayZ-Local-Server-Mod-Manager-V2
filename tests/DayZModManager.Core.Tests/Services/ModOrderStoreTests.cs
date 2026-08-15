using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class ModOrderStoreTests
{
    [Fact]
    public void Load_ReturnsMissing_WhenFileAbsent()
    {
        var store = new ModOrderStore(new FakeFileSystem());

        Assert.Equal(ConfigLoadStatus.Missing, store.Load(@"C:\data").Status);
    }

    [Fact]
    public void Load_ReturnsCorrupt_WhenJsonInvalid()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\data\mod_order.json", "not json");
        var store = new ModOrderStore(fs);

        Assert.Equal(ConfigLoadStatus.Corrupt, store.Load(@"C:\data").Status);
    }

    [Fact]
    public void Save_ThenLoad_PreservesOrder()
    {
        var fs = new FakeFileSystem();
        var store = new ModOrderStore(fs);

        store.Save(@"C:\data", new[] { "@CF", "@Dabs Framework", "@VPPAdminTools" });
        ConfigLoadResult<IReadOnlyList<string>> result = store.Load(@"C:\data");

        Assert.Equal(ConfigLoadStatus.Success, result.Status);
        Assert.Equal(new[] { "@CF", "@Dabs Framework", "@VPPAdminTools" }, result.Value!);
    }
}
