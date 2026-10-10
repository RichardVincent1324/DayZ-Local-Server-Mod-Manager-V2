using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class TypesConfigStoreTests
{
    private const string PresetDir = @"D:\app\data\Presets\dayzOffline.chernarusplus\__default_preset__";

    [Fact]
    public void Save_PersistsOnlyTheCurrentMap()
    {
        var fs = new FakeFileSystem();
        var store = new TypesConfigStore(fs);
        var config = new TypesConfig
        {
            CurrentMap = "mapA",
            Maps =
            {
                ["mapA"] = new MapTypesConfig { Mods = { new ModTypesEntry { ModName = "@A" } } },
                ["mapB"] = new MapTypesConfig { Mods = { new ModTypesEntry { ModName = "@B" } } },
            },
        };

        store.Save(PresetDir, config);

        TypesConfig reloaded = store.Load(PresetDir).Value!;
        Assert.Equal("mapA", reloaded.CurrentMap);
        Assert.True(reloaded.Maps.ContainsKey("mapA"));
        Assert.False(reloaded.Maps.ContainsKey("mapB"), "another map's config must never be written into this preset");
    }

    [Fact]
    public void Save_WithCurrentMapWithoutEntry_WritesEmptyConfigForThatMap()
    {
        var fs = new FakeFileSystem();
        var store = new TypesConfigStore(fs);
        var config = new TypesConfig
        {
            CurrentMap = "mapA",
            Maps = { ["mapB"] = new MapTypesConfig() },
        };

        store.Save(PresetDir, config);

        TypesConfig reloaded = store.Load(PresetDir).Value!;
        Assert.Equal("mapA", reloaded.CurrentMap);
        Assert.Empty(reloaded.Maps);
    }
}
