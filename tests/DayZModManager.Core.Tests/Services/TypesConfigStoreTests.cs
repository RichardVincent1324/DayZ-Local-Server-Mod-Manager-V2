using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class TypesConfigStoreTests
{
    private const string PresetDir = @"D:\app\data\Presets\dayzOffline.chernarusplus\__default_preset__";

    [Fact]
    public void Save_ThenLoad_PreservesMods()
    {
        var fs = new FakeFileSystem();
        var store = new TypesConfigStore(fs);
        var config = new TypesConfig
        {
            Mods = { new ModTypesEntry { ModName = "@A" } },
        };

        store.Save(PresetDir, config);

        TypesConfig reloaded = store.Load(PresetDir).Value!;
        Assert.Equal("@A", reloaded.Mods.Single().ModName);
    }

    [Fact]
    public void Save_WritesMapAgnosticShape()
    {
        var fs = new FakeFileSystem();
        var store = new TypesConfigStore(fs);

        store.Save(PresetDir, new TypesConfig { Mods = { new ModTypesEntry { ModName = "@A" } } });

        string json = fs.TryGetFileContents(Path.Combine(PresetDir, "types_config.json"))!;
        Assert.Contains("\"mods\"", json);
        Assert.DoesNotContain("\"maps\"", json);
        Assert.DoesNotContain("\"currentMap\"", json);
    }

    [Fact]
    public void Load_RejectsOldMapEnvelope()
    {
        var fs = new FakeFileSystem();
        string path = Path.Combine(PresetDir, "types_config.json");
        fs.AddFile(path, "{\"maps\":{\"mapA\":{\"mods\":[]}},\"currentMap\":\"mapA\"}");
        var store = new TypesConfigStore(fs);

        Assert.Equal(ConfigLoadStatus.Corrupt, store.Load(PresetDir).Status);
        Assert.True(store.BackupCorrupt(PresetDir));
        Assert.Equal(
            "{\"maps\":{\"mapA\":{\"mods\":[]}},\"currentMap\":\"mapA\"}",
            fs.TryGetFileContents(path + ".corrupt"));
    }

    [Fact]
    public void BackupCorrupt_PreservesUnreadableFile()
    {
        var fs = new FakeFileSystem();
        string path = Path.Combine(PresetDir, "types_config.json");
        fs.AddFile(path, "not json");
        var store = new TypesConfigStore(fs);

        Assert.True(store.BackupCorrupt(PresetDir));
        Assert.Equal("not json", fs.TryGetFileContents(path + ".corrupt"));
    }

    [Fact]
    public void BackupCorrupt_ReturnsFalse_ForValidFile()
    {
        var fs = new FakeFileSystem();
        var store = new TypesConfigStore(fs);
        store.Save(PresetDir, new TypesConfig());

        Assert.False(store.BackupCorrupt(PresetDir));
        Assert.False(fs.FileExists(Path.Combine(PresetDir, "types_config.json.corrupt")));
    }

    [Fact]
    public void Save_BacksUpCorruptFileBeforeWritingNewConfig()
    {
        var fs = new FakeFileSystem();
        string path = Path.Combine(PresetDir, "types_config.json");
        fs.AddFile(path, "not json");
        var store = new TypesConfigStore(fs);
        var config = new TypesConfig { IsCorrupt = true };

        store.Save(PresetDir, config);

        Assert.Equal("not json", fs.TryGetFileContents(path + ".corrupt"));
        Assert.Equal(ConfigLoadStatus.Success, store.Load(PresetDir).Status);
        Assert.False(config.IsCorrupt);
    }
}
