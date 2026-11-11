using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class TypeFilesConfigStoreTests
{
    private const string PresetDir = @"D:\app\data\Presets\dayzOffline.chernarusplus\__default_preset__";
    private const string TypeFilesDir = PresetDir + @"\type_files";

    private static FakeFileSystem CreateFileSystem(params string[] fileNames)
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(TypeFilesDir);
        foreach (string name in fileNames)
        {
            fs.AddFile(Path.Combine(TypeFilesDir, name), "<types/>");
        }

        return fs;
    }

    [Fact]
    public void Save_ThenLoad_PreservesAssignments()
    {
        FakeFileSystem fs = CreateFileSystem("00_base.xml", "99_override.xml");
        var store = new TypeFilesConfigStore(fs);

        store.Save(PresetDir, new Dictionary<string, string>
        {
            ["00_base.xml"] = "types",
            ["99_override.xml"] = "spawnabletypes",
        });

        Dictionary<string, string> reloaded = store.Load(PresetDir).Value!;
        Assert.Equal("types", reloaded["00_base.xml"]);
        Assert.Equal("spawnabletypes", reloaded["99_override.xml"]);
    }

    [Fact]
    public void Save_WritesRootLevelDictionary()
    {
        FakeFileSystem fs = CreateFileSystem("00_base.xml");
        var store = new TypeFilesConfigStore(fs);

        store.Save(PresetDir, new Dictionary<string, string> { ["00_base.xml"] = "types" });

        string json = fs.TryGetFileContents(Path.Combine(PresetDir, "type_files.json"))!;
        Assert.Contains("\"00_base.xml\"", json);
        Assert.Contains("\"types\"", json);
        Assert.DoesNotContain("\"assignments\"", json);
    }

    [Fact]
    public void Load_RejectsNonObjectRoot()
    {
        var fs = new FakeFileSystem();
        string path = Path.Combine(PresetDir, "type_files.json");
        fs.AddFile(path, "[]");
        var store = new TypeFilesConfigStore(fs);

        Assert.Equal(ConfigLoadStatus.Corrupt, store.Load(PresetDir).Status);
        Assert.True(store.BackupCorrupt(PresetDir));
        Assert.Equal("[]", fs.TryGetFileContents(path + ".corrupt"));
    }

    [Fact]
    public void BackupCorrupt_PreservesUnreadableFile()
    {
        var fs = new FakeFileSystem();
        string path = Path.Combine(PresetDir, "type_files.json");
        fs.AddFile(path, "not json");
        var store = new TypeFilesConfigStore(fs);

        Assert.True(store.BackupCorrupt(PresetDir));
        Assert.Equal("not json", fs.TryGetFileContents(path + ".corrupt"));
    }

    [Fact]
    public void Save_BacksUpCorruptFileBeforeWritingNewConfig()
    {
        FakeFileSystem fs = CreateFileSystem("00_base.xml");
        string path = Path.Combine(PresetDir, "type_files.json");
        fs.AddFile(path, "not json");
        var store = new TypeFilesConfigStore(fs);

        store.Save(PresetDir, new Dictionary<string, string> { ["00_base.xml"] = "types" });

        Assert.Equal("not json", fs.TryGetFileContents(path + ".corrupt"));
        Assert.Equal(ConfigLoadStatus.Success, store.Load(PresetDir).Status);
    }

    [Fact]
    public void Save_PrunesEntriesWhoseFilesNoLongerExist()
    {
        FakeFileSystem fs = CreateFileSystem("still_there.xml");
        fs.AddFile(Path.Combine(PresetDir, "type_files.json"),
            "{\"still_there.xml\":\"types\",\"gone.xml\":\"types\"}");
        var store = new TypeFilesConfigStore(fs);

        store.Save(PresetDir, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["still_there.xml"] = "types",
            ["gone.xml"] = "types",
        });

        Dictionary<string, string> reloaded = store.Load(PresetDir).Value!;
        Assert.Contains("still_there.xml", reloaded.Keys);
        Assert.DoesNotContain("gone.xml", reloaded.Keys);
    }

    [Fact]
    public void Save_DropsUnknownRoleValues()
    {
        FakeFileSystem fs = CreateFileSystem("a.xml", "b.xml", "c.xml");
        var store = new TypeFilesConfigStore(fs);

        store.Save(PresetDir, new Dictionary<string, string>
        {
            ["a.xml"] = "types",
            ["b.xml"] = "Unassigned",
            ["c.xml"] = string.Empty,
        });

        Dictionary<string, string> reloaded = store.Load(PresetDir).Value!;
        Assert.Equal("types", reloaded["a.xml"]);
        Assert.False(reloaded.ContainsKey("b.xml"));
        Assert.False(reloaded.ContainsKey("c.xml"));
    }

    [Fact]
    public void Load_IgnoresUnknownRoleValues()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(Path.Combine(PresetDir, "type_files.json"),
            "{\"a.xml\":\"types\",\"b.xml\":\"garbage\",\"c.xml\":5}");
        var store = new TypeFilesConfigStore(fs);

        Dictionary<string, string> loaded = store.Load(PresetDir).Value!;
        Assert.Equal("types", loaded["a.xml"]);
        Assert.False(loaded.ContainsKey("b.xml"));
        Assert.False(loaded.ContainsKey("c.xml"));
    }

    [Fact]
    public void Save_Aborts_WhenTypeFilesFolderMissing()
    {
        var fs = new FakeFileSystem();
        var store = new TypeFilesConfigStore(fs);

        Assert.Throws<IOException>(() => store.Save(PresetDir, new Dictionary<string, string>()));
        Assert.False(fs.FileExists(Path.Combine(PresetDir, "type_files.json")));
    }

    [Fact]
    public void Save_Aborts_WhenTypeFilesFolderCannotBeRead()
    {
        FakeFileSystem fs = CreateFileSystem("00_base.xml");
        fs.ThrowOnGetFiles = true;
        var store = new TypeFilesConfigStore(fs);

        Assert.Throws<IOException>(() => store.Save(PresetDir, new Dictionary<string, string>()));
        Assert.False(fs.FileExists(Path.Combine(PresetDir, "type_files.json")));
    }

    [Fact]
    public void Save_UsesCaseInsensitiveKeys()
    {
        FakeFileSystem fs = CreateFileSystem("Base.xml");
        var store = new TypeFilesConfigStore(fs);

        store.Save(PresetDir, new Dictionary<string, string> { ["base.xml"] = "types" });

        Dictionary<string, string> reloaded = store.Load(PresetDir).Value!;
        Assert.Equal("types", reloaded["BASE.XML"]);
    }
}
