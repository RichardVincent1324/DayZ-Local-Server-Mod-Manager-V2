using DayZModManager.Core.Models;

namespace DayZModManager.Core.Tests.Models;

public class TypesConfigMergeTests
{
    [Fact]
    public void MergeFrom_ReplacesOnlyProvidedMaps()
    {
        var target = new TypesConfig
        {
            CurrentMap = "a",
            Maps = { ["a"] = new MapTypesConfig(), ["b"] = new MapTypesConfig() },
        };
        var loaded = new TypesConfig
        {
            CurrentMap = "a",
            Maps = { ["a"] = new MapTypesConfig { Mods = { new ModTypesEntry { ModName = "@New" } } } },
        };

        target.MergeFrom(loaded);

        Assert.Equal("a", target.CurrentMap);
        Assert.Equal("@New", target.Maps["a"].Mods.Single().ModName);
        Assert.True(target.Maps.ContainsKey("b"), "unrelated maps must be preserved");
    }

    [Fact]
    public void MergeFrom_ClearsMap_WhenLoadedScopedConfigHasNoEntry()
    {
        var target = new TypesConfig { Maps = { ["a"] = new MapTypesConfig() } };
        var loaded = new TypesConfig { CurrentMap = "a" };

        target.MergeFrom(loaded);

        Assert.False(target.Maps.ContainsKey("a"));
        Assert.Equal("a", target.CurrentMap);
    }

    [Fact]
    public void MergeFrom_Null_DoesNothing()
    {
        var target = new TypesConfig { CurrentMap = "a", Maps = { ["a"] = new MapTypesConfig() } };

        target.MergeFrom(null!);

        Assert.Equal("a", target.CurrentMap);
        Assert.True(target.Maps.ContainsKey("a"));
    }
}
