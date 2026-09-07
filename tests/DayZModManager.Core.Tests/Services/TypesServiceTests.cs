using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class TypesServiceTests
{
    private const string WorkshopPath = @"D:\DayZ\!Workshop";
    private const string MissionPath = @"D:\DayZServer\mpmissions\dayzOffline.chernarusplus";
    private const string MapName = "dayzOffline.chernarusplus";

    private const string EconomyCoreXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
        <economycore>
        	<classes>
        		<rootclass name="DefaultWeapon" />
        	</classes>
        </economycore>
        """;

    private static TypesService CreateService(FakeFileSystem fs) =>
        new(fs, new EconomyCoreService(fs));

    private static HashSet<string> Loaded(params string[] mods) => new(mods, StringComparer.Ordinal);

    private static FakeFileSystem Seed()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory($@"{WorkshopPath}\@CF");
        fs.AddFile($@"{WorkshopPath}\@CF\types.xml", "<types/>");
        fs.AddFile($@"{WorkshopPath}\@CF\cfgspawnabletypes.xml", "<spawnabletypes/>");
        fs.AddFile($@"{WorkshopPath}\@CF\economy.xml", "<economy/>");
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", EconomyCoreXml);
        return fs;
    }

    private static FakeFileSystem SeedTwoMods()
    {
        var fs = Seed();
        fs.AddDirectory($@"{WorkshopPath}\@OtherMod");
        fs.AddFile($@"{WorkshopPath}\@OtherMod\types.xml", "<types/>");
        return fs;
    }

    [Fact]
    public void DiscoverTypeFiles_FindsOnlyTypeXmlFiles()
    {
        FakeFileSystem fs = Seed();

        IReadOnlyList<string> files = CreateService(fs).DiscoverTypeFiles(WorkshopPath, "@CF");

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.EndsWith("types.xml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.EndsWith("cfgspawnabletypes.xml", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.EndsWith("economy.xml", StringComparison.OrdinalIgnoreCase));
    }

    private static TypesConfig ConfigWith(params ModTypesEntry[] entries) =>
        new() { Maps = { [MapName] = new MapTypesConfig { Mods = entries.ToList() } } };

    private static ModTypesEntry Entry(string modName, params string[] generated) =>
        new() { ModName = modName, GeneratedFiles = generated.ToList() };

    [Fact]
    public void GetActiveTypeFileNames_ReturnsLeavesInEconomyCoreOrder()
    {
        TypesConfig config = ConfigWith(
            Entry("@CF", @"db\ModTypes\CF_cfgspawnabletypes.xml", @"db\ModTypes\CF_types.xml"));

        IReadOnlyList<string> names = CreateService(Seed()).GetActiveTypeFileNames(config, MapName, Loaded("@CF"));

        Assert.Equal(new[] { "CF_types.xml", "CF_cfgspawnabletypes.xml" }, names);
    }

    [Fact]
    public void GetActiveTypeFileNames_FiltersToLoadedMods()
    {
        TypesConfig config = ConfigWith(
            Entry("@CF", @"db\ModTypes\CF_types.xml", @"db\ModTypes\CF_cfgspawnabletypes.xml"),
            Entry("@Other", @"db\ModTypes\Other_types.xml"));

        IReadOnlyList<string> names = CreateService(Seed()).GetActiveTypeFileNames(config, MapName, Loaded("@CF"));

        Assert.Equal(new[] { "CF_types.xml", "CF_cfgspawnabletypes.xml" }, names);
    }

    [Fact]
    public void GetActiveTypeFileNames_ReturnsEmpty_WhenNoMapConfig()
    {
        var config = new TypesConfig();

        IReadOnlyList<string> names = CreateService(Seed()).GetActiveTypeFileNames(config, MapName, Loaded("@CF"));

        Assert.Empty(names);
    }

    [Fact]
    public void ConfigureMod_CopiesFilesAndUpdatesConfig()
    {
        FakeFileSystem fs = Seed();
        var config = new TypesConfig();
        var sourceFiles = new[]
        {
            $@"{WorkshopPath}\@CF\types.xml",
            $@"{WorkshopPath}\@CF\cfgspawnabletypes.xml",
        };

        TypesOperationResult result = CreateService(fs)
            .ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF", sourceFiles, Loaded("@CF"));

        Assert.True(result.Success);

        ModTypesEntry entry = config.Maps[MapName].Mods.Single();
        Assert.Equal("@CF", entry.ModName);
        Assert.Equal(new[] { "types.xml", "cfgspawnabletypes.xml" }, entry.SourceFiles);
        Assert.Equal(
            new[] { @"db\ModTypes\CF_types.xml", @"db\ModTypes\CF_cfgspawnabletypes.xml" },
            entry.GeneratedFiles);

        Assert.NotNull(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\CF_types.xml"));
        Assert.NotNull(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\CF_cfgspawnabletypes.xml"));

        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("CF_types.xml", economy);
        Assert.Contains("spawnabletypes", economy);
    }

    [Fact]
    public void ConfigureMod_ReplacesPreviousEntryAndDeletesOldFiles()
    {
        FakeFileSystem fs = Seed();
        var config = new TypesConfig();
        var service = CreateService(fs);

        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, Loaded("@CF"));
        string oldGenerated = config.Maps[MapName].Mods.Single().GeneratedFiles[0];
        Assert.NotNull(fs.TryGetFileContents($@"{MissionPath}\{oldGenerated}"));

        // Reconfigure with only the spawnable file.
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[] { $@"{WorkshopPath}\@CF\cfgspawnabletypes.xml" }, Loaded("@CF"));

        ModTypesEntry entry = config.Maps[MapName].Mods.Single();
        Assert.Single(entry.GeneratedFiles);
        Assert.Null(fs.TryGetFileContents($@"{MissionPath}\{oldGenerated}"));
        Assert.NotNull(fs.TryGetFileContents($@"{MissionPath}\{entry.GeneratedFiles[0]}"));
    }

    [Fact]
    public void RemoveFiles_RemovesOnlyRequestedFiles_AndEntryWhenEmpty()
    {
        FakeFileSystem fs = Seed();
        var config = new TypesConfig();
        var service = CreateService(fs);

        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[] { $@"{WorkshopPath}\@CF\types.xml", $@"{WorkshopPath}\@CF\cfgspawnabletypes.xml" }, Loaded("@CF"));

        var leaves = new HashSet<string>(new[] { "CF_types.xml" }, StringComparer.Ordinal);
        TypesOperationResult result = service.RemoveFiles(config, MapName, MissionPath, "@CF", leaves, Loaded("@CF"));

        Assert.True(result.Success);
        Assert.Null(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\CF_types.xml"));
        Assert.NotNull(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\CF_cfgspawnabletypes.xml"));
        Assert.Single(config.Maps[MapName].Mods.Single().GeneratedFiles);
    }

    [Fact]
    public void CleanInvalid_RemovesEntriesForMissingMods()
    {
        FakeFileSystem fs = Seed();
        var config = new TypesConfig();
        var service = CreateService(fs);

        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, Loaded("@CF"));

        var valid = new HashSet<string>(new[] { "@OtherMod" }, StringComparer.Ordinal);
        TypesOperationResult result = service.CleanInvalid(config, MapName, MissionPath, valid, Loaded("@CF"));

        Assert.True(result.Success);
        Assert.Empty(config.Maps[MapName].Mods);
        Assert.Null(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\CF_types.xml"));
    }

    [Fact]
    public void CleanInvalid_RemovesUnloadedModConfigs()
    {
        FakeFileSystem fs = SeedTwoMods();
        var config = new TypesConfig();
        var service = CreateService(fs);

        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, Loaded("@CF", "@OtherMod"));
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@OtherMod",
            new[] { $@"{WorkshopPath}\@OtherMod\types.xml" }, Loaded("@CF", "@OtherMod"));

        // @OtherMod is unloaded: only @CF remains active.
        var active = new HashSet<string>(new[] { "@CF" }, StringComparer.Ordinal);
        TypesOperationResult result = service.CleanInvalid(config, MapName, MissionPath, active, Loaded("@CF"));

        Assert.True(result.Success);
        Assert.Single(config.Maps[MapName].Mods);
        Assert.Equal("@CF", config.Maps[MapName].Mods.Single().ModName);
        Assert.Null(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\OtherMod_types.xml"));

        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("CF_types.xml", economy);
        Assert.DoesNotContain("OtherMod_types.xml", economy);
    }

    [Fact]
    public void SyncEconomyCore_IncludesOnlyLoadedMods()
    {
        FakeFileSystem fs = SeedTwoMods();
        var config = new TypesConfig();
        var service = CreateService(fs);

        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, Loaded("@CF", "@OtherMod"));
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@OtherMod",
            new[] { $@"{WorkshopPath}\@OtherMod\types.xml" }, Loaded("@CF", "@OtherMod"));

        Assert.True(service.SyncEconomyCore(config, MapName, MissionPath, Loaded("@CF")));

        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("CF_types.xml", economy);
        Assert.DoesNotContain("OtherMod_types.xml", economy);
    }

    [Fact]
    public void ConfigureMod_ExcludesUnloadedModsFromEconomy()
    {
        FakeFileSystem fs = SeedTwoMods();
        var config = new TypesConfig();
        var service = CreateService(fs);

        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, Loaded("@CF", "@OtherMod"));
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@OtherMod",
            new[] { $@"{WorkshopPath}\@OtherMod\types.xml" }, Loaded("@CF", "@OtherMod"));

        // Reconfigure @CF with @OtherMod no longer loaded.
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, Loaded("@CF"));

        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("CF_types.xml", economy);
        Assert.DoesNotContain("OtherMod_types.xml", economy);
    }

    [Fact]
    public void ConfigureMod_OrdersTypesBeforeSpawnableTypesWithinMod()
    {
        FakeFileSystem fs = Seed();
        var config = new TypesConfig();
        var service = CreateService(fs);

        // Supply the spawnable file first; the generated cfgeconomycore.xml must
        // still list the regular types file before the spawnabletypes file.
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[]
            {
                $@"{WorkshopPath}\@CF\cfgspawnabletypes.xml",
                $@"{WorkshopPath}\@CF\types.xml",
            }, Loaded("@CF"));

        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        int typesIndex = economy.IndexOf("CF_types.xml", StringComparison.Ordinal);
        int spawnableIndex = economy.IndexOf("CF_cfgspawnabletypes.xml", StringComparison.Ordinal);

        Assert.True(typesIndex >= 0, "types file missing");
        Assert.True(spawnableIndex >= 0, "spawnabletypes file missing");
        Assert.True(typesIndex < spawnableIndex, "types must precede spawnabletypes");
    }

    [Fact]
    public void GetGeneratedFileName_RootFile()
    {
        FakeFileSystem fs = Seed();

        string? leaf = CreateService(fs)
            .GetGeneratedFileName(WorkshopPath, "@CF", $@"{WorkshopPath}\@CF\types.xml");

        Assert.Equal("CF_types.xml", leaf);
    }

    [Fact]
    public void GetGeneratedFileName_NestedFile_UsesUnderscores()
    {
        FakeFileSystem fs = Seed();
        string source = $@"{WorkshopPath}\@InediaInfectedAI\Hardcore\types.xml";

        string? leaf = CreateService(fs)
            .GetGeneratedFileName(WorkshopPath, "@InediaInfectedAI", source);

        Assert.Equal("InediaInfectedAI_Hardcore_types.xml", leaf);
    }

    [Fact]
    public void GetGeneratedFileName_ReturnsNull_WhenSourceOutsideMod()
    {
        FakeFileSystem fs = Seed();

        string? leaf = CreateService(fs)
            .GetGeneratedFileName(WorkshopPath, "@CF", @"D:\elsewhere\types.xml");

        Assert.Null(leaf);
    }

    [Fact]
    public void RemoveUntrackedFiles_DeletesFileAndDropsEconomyReference()
    {
        FakeFileSystem fs = Seed();
        fs.AddDirectory($@"{MissionPath}\db", "ModTypes");
        fs.AddFile($@"{MissionPath}\db\ModTypes\CF_types.xml", "<types/>");
        fs.AddFile($@"{MissionPath}\db\ModTypes\Orphan_types.xml", "<types/>");
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/ModTypes">
            		<file name="CF_types.xml" type="types" />
            		<file name="Orphan_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        TypesConfig config = ConfigWith(Entry("@CF", @"db\ModTypes\CF_types.xml"));
        TypesService service = CreateService(fs);

        TypesOperationResult result = service.RemoveUntrackedFiles(
            config, MapName, MissionPath, new HashSet<string> { "Orphan_types.xml" });

        Assert.True(result.Success);
        Assert.False(fs.FileExists($@"{MissionPath}\db\ModTypes\Orphan_types.xml"));
        Assert.True(fs.FileExists($@"{MissionPath}\db\ModTypes\CF_types.xml"));
        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.DoesNotContain("Orphan_types.xml", economy);
        Assert.Contains("CF_types.xml", economy);
        Assert.Contains(result.Messages, m => m.Contains("Orphan_types.xml"));
    }

    [Fact]
    public void RemoveUntrackedFiles_NeverTouchesTrackedFiles()
    {
        FakeFileSystem fs = Seed();
        fs.AddDirectory($@"{MissionPath}\db", "ModTypes");
        fs.AddFile($@"{MissionPath}\db\ModTypes\CF_types.xml", "<types/>");
        fs.AddFile($@"{MissionPath}\db\ModTypes\Orphan_types.xml", "<types/>");
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/ModTypes">
            		<file name="CF_types.xml" type="types" />
            		<file name="Orphan_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        TypesConfig config = ConfigWith(Entry("@CF", @"db\ModTypes\CF_types.xml"));
        TypesService service = CreateService(fs);

        // Requesting a tracked file must be ignored; only the orphan is removed.
        TypesOperationResult result = service.RemoveUntrackedFiles(
            config, MapName, MissionPath, new HashSet<string> { "CF_types.xml", "Orphan_types.xml" });

        Assert.True(result.Success);
        Assert.True(fs.FileExists($@"{MissionPath}\db\ModTypes\CF_types.xml"));
        Assert.False(fs.FileExists($@"{MissionPath}\db\ModTypes\Orphan_types.xml"));
        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("CF_types.xml", economy);
        Assert.DoesNotContain("Orphan_types.xml", economy);
    }

    [Fact]
    public void RemoveUntrackedFiles_ReturnsFailure_WhenEconomyCoreMalformed()
    {
        FakeFileSystem fs = Seed();
        fs.AddDirectory($@"{MissionPath}\db", "ModTypes");
        fs.AddFile($@"{MissionPath}\db\ModTypes\Orphan_types.xml", "<types/>");
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", "<economycore>");
        TypesConfig config = ConfigWith();
        TypesService service = CreateService(fs);

        TypesOperationResult result = service.RemoveUntrackedFiles(
            config, MapName, MissionPath, new HashSet<string> { "Orphan_types.xml" });

        Assert.False(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("cfgeconomycore.xml"));
        // The physical file is only deleted after the economy update succeeds.
        Assert.True(fs.FileExists($@"{MissionPath}\db\ModTypes\Orphan_types.xml"));
    }

    [Fact]
    public void ConfigureMod_EconomyMissing_FailsWithoutLeavingFilesOrConfig()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory($@"{WorkshopPath}\@CF");
        fs.AddFile($@"{WorkshopPath}\@CF\types.xml", "<types/>");
        var config = new TypesConfig();
        TypesService service = CreateService(fs);

        TypesOperationResult result = service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, Loaded("@CF"));

        Assert.False(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("cfgeconomycore.xml"));
        Assert.False(fs.FileExists($@"{MissionPath}\db\ModTypes\CF_types.xml"));
        Assert.Empty(config.Maps[MapName].Mods);
    }

    [Fact]
    public void RemoveFiles_EconomyUnreadable_FailsWithoutDeletingFilesOrConfig()
    {
        FakeFileSystem fs = Seed();
        var config = new TypesConfig();
        var service = CreateService(fs);
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, Loaded("@CF"));

        // Corrupt the economy file so the rewrite that would follow reports failure.
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", "<economycore>");
        TypesOperationResult result = service.RemoveFiles(config, MapName, MissionPath, "@CF",
            new HashSet<string> { "CF_types.xml" }, Loaded("@CF"));

        Assert.False(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("cfgeconomycore.xml"));
        Assert.NotNull(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\CF_types.xml"));
        Assert.Single(config.Maps[MapName].Mods.Single().GeneratedFiles);
    }

    [Fact]
    public void CleanInvalid_EconomyUnreadable_FailsWithoutDeletingFilesOrConfig()
    {
        FakeFileSystem fs = Seed();
        var config = new TypesConfig();
        var service = CreateService(fs);
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, Loaded("@CF"));

        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", "<economycore>");
        TypesOperationResult result = service.CleanInvalid(config, MapName, MissionPath,
            new HashSet<string> { "@OtherMod" }, Loaded("@CF"));

        Assert.False(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("cfgeconomycore.xml"));
        Assert.NotNull(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\CF_types.xml"));
        Assert.Single(config.Maps[MapName].Mods);
    }
}
