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

    private static IReadOnlyList<TypeFileSelection> Select(params string[] sources) =>
        sources.Select(s => new TypeFileSelection(s, TypesFileRole.Types)).ToList();

    private static IReadOnlyList<TypeFileSelection> Select(string source, TypesFileRole role) =>
        new[] { new TypeFileSelection(source, role) };

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
    public void DiscoverXmlFiles_FindsAllXmlFiles()
    {
        FakeFileSystem fs = Seed();

        IReadOnlyList<string> files = CreateService(fs).DiscoverXmlFiles(WorkshopPath, "@CF");

        Assert.Equal(3, files.Count);
        Assert.Contains(files, f => f.EndsWith("types.xml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.EndsWith("cfgspawnabletypes.xml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.EndsWith("economy.xml", StringComparison.OrdinalIgnoreCase));
    }

    private static TypesConfig ConfigWith(params ModTypesEntry[] entries) =>
        new() { Maps = { [MapName] = new MapTypesConfig { Mods = entries.ToList() } } };

    private static ModTypesEntry Entry(string modName, params string[] generated)
    {
        var entry = new ModTypesEntry { ModName = modName, GeneratedFiles = generated.ToList() };
        foreach (string file in generated)
        {
            string leaf = Path.GetFileName(file);
            entry.FileRoles[leaf] = leaf.Contains("spawnable", StringComparison.OrdinalIgnoreCase)
                ? "spawnabletypes"
                : "types";
        }

        return entry;
    }

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
        IReadOnlyList<TypeFileSelection> sourceFiles = Select(
            $@"{WorkshopPath}\@CF\types.xml",
            $@"{WorkshopPath}\@CF\cfgspawnabletypes.xml");

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
            Select($@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF"));
        string oldGenerated = config.Maps[MapName].Mods.Single().GeneratedFiles[0];
        Assert.NotNull(fs.TryGetFileContents($@"{MissionPath}\{oldGenerated}"));

        // Reconfigure with only the spawnable file.
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            Select($@"{WorkshopPath}\@CF\cfgspawnabletypes.xml"), Loaded("@CF"));

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
            Select($@"{WorkshopPath}\@CF\types.xml", $@"{WorkshopPath}\@CF\cfgspawnabletypes.xml"), Loaded("@CF"));

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
            Select($@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF"));

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
            Select($@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF", "@OtherMod"));
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@OtherMod",
            Select($@"{WorkshopPath}\@OtherMod\types.xml"), Loaded("@CF", "@OtherMod"));

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
            Select($@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF", "@OtherMod"));
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@OtherMod",
            Select($@"{WorkshopPath}\@OtherMod\types.xml"), Loaded("@CF", "@OtherMod"));

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
            Select($@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF", "@OtherMod"));
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@OtherMod",
            Select($@"{WorkshopPath}\@OtherMod\types.xml"), Loaded("@CF", "@OtherMod"));

        // Reconfigure @CF with @OtherMod no longer loaded.
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            Select($@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF"));

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
            Select(
                $@"{WorkshopPath}\@CF\cfgspawnabletypes.xml",
                $@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF"));

        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        int typesIndex = economy.IndexOf("CF_types.xml", StringComparison.Ordinal);
        int spawnableIndex = economy.IndexOf("CF_cfgspawnabletypes.xml", StringComparison.Ordinal);

        Assert.True(typesIndex >= 0, "types file missing");
        Assert.True(spawnableIndex >= 0, "spawnabletypes file missing");
        Assert.True(typesIndex < spawnableIndex, "types must precede spawnabletypes");
    }

    [Fact]
    public void ConfigureMod_UnrecognizedFileAssignedTypes_UsesNaturalName()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile($@"{WorkshopPath}\@CF\settings.xml", "<settings/>");
        var config = new TypesConfig();

        CreateService(fs).ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            Select($@"{WorkshopPath}\@CF\settings.xml", TypesFileRole.Types), Loaded("@CF"));

        ModTypesEntry entry = config.Maps[MapName].Mods.Single();
        Assert.Contains(@"db\ModTypes\CF_settings.xml", entry.GeneratedFiles);
        Assert.NotNull(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\CF_settings.xml"));

        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("CF_settings.xml\" type=\"types\"", economy);
    }

    [Fact]
    public void ConfigureMod_UnrecognizedFileAssignedSpawnable_UsesNaturalNameAndOrdersLast()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile($@"{WorkshopPath}\@CF\settings.xml", "<settings/>");
        var config = new TypesConfig();

        // Supply the unrecognized spawnable file first; the regular types file
        // must still be listed before it in cfgeconomycore.xml.
        CreateService(fs).ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[]
            {
                new TypeFileSelection($@"{WorkshopPath}\@CF\settings.xml", TypesFileRole.SpawnableTypes),
                new TypeFileSelection($@"{WorkshopPath}\@CF\types.xml", TypesFileRole.Types),
            }, Loaded("@CF"));

        ModTypesEntry entry = config.Maps[MapName].Mods.Single();
        Assert.Contains(@"db\ModTypes\CF_settings.xml", entry.GeneratedFiles);
        Assert.NotNull(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\CF_settings.xml"));

        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        int typesIndex = economy.IndexOf("CF_types.xml", StringComparison.Ordinal);
        int spawnIndex = economy.IndexOf("CF_settings.xml", StringComparison.Ordinal);
        Assert.True(typesIndex >= 0, "types file missing");
        Assert.True(spawnIndex >= 0, "spawnable file missing");
        Assert.True(typesIndex < spawnIndex, "types must precede the spawnable file");
        Assert.Contains("CF_settings.xml\" type=\"spawnabletypes\"", economy);
    }

    [Fact]
    public void ConfigureMod_RecognizedFile_IgnoresRequestedRole()
    {
        FakeFileSystem fs = Seed();
        var config = new TypesConfig();

        // A recognized "types" file cannot be reassigned as spawnable.
        CreateService(fs).ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            Select($@"{WorkshopPath}\@CF\types.xml", TypesFileRole.SpawnableTypes), Loaded("@CF"));

        ModTypesEntry entry = config.Maps[MapName].Mods.Single();
        Assert.Contains(@"db\ModTypes\CF_types.xml", entry.GeneratedFiles);
        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("CF_types.xml\" type=\"types\"", economy);
    }

    [Fact]
    public void ConfigureMod_StoresFileRoles()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile($@"{WorkshopPath}\@CF\weird.xml", "<weird/>");
        var config = new TypesConfig();

        CreateService(fs).ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[]
            {
                new TypeFileSelection($@"{WorkshopPath}\@CF\types.xml", TypesFileRole.Types),
                new TypeFileSelection($@"{WorkshopPath}\@CF\weird.xml", TypesFileRole.SpawnableTypes),
            }, Loaded("@CF"));

        ModTypesEntry entry = config.Maps[MapName].Mods.Single();
        Assert.Equal("types", entry.FileRoles["CF_types.xml"]);
        Assert.Equal("spawnabletypes", entry.FileRoles["CF_weird.xml"]);
    }

    [Fact]
    public void ConfigureMod_UnrecognizedTypesInSpawnableDirectory_WritesTypes()
    {
        // A "spawnable" directory keyword must not override the chosen role.
        FakeFileSystem fs = Seed();
        fs.AddFile($@"{WorkshopPath}\@CF\spawnable\weird.xml", "<weird/>");
        var config = new TypesConfig();

        CreateService(fs).ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            Select($@"{WorkshopPath}\@CF\spawnable\weird.xml", TypesFileRole.Types), Loaded("@CF"));

        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("CF_spawnable_weird.xml\" type=\"types\"", economy);
    }

    [Fact]
    public void ConfigureMod_RecognizedTypesInSpawnableDirectory_WritesTypes()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile($@"{WorkshopPath}\@CF\spawnable\types.xml", "<types/>");
        var config = new TypesConfig();

        CreateService(fs).ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            Select($@"{WorkshopPath}\@CF\spawnable\types.xml", TypesFileRole.Types), Loaded("@CF"));

        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("CF_spawnable_types.xml\" type=\"types\"", economy);
    }

    [Fact]
    public void ConfigureMod_ModNameContainsSpawnable_WritesTypes()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory($@"{WorkshopPath}\@SpawnableItems");
        fs.AddFile($@"{WorkshopPath}\@SpawnableItems\types.xml", "<types/>");
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", EconomyCoreXml);
        var config = new TypesConfig();

        CreateService(fs).ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@SpawnableItems",
            Select($@"{WorkshopPath}\@SpawnableItems\types.xml", TypesFileRole.Types), Loaded("@SpawnableItems"));

        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("SpawnableItems_types.xml\" type=\"types\"", economy);
    }

    [Fact]
    public void GetConfiguredFiles_ReturnsSourceLeafAndRole()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile($@"{WorkshopPath}\@CF\weird.xml", "<weird/>");
        var config = new TypesConfig();

        CreateService(fs).ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            Select($@"{WorkshopPath}\@CF\weird.xml", TypesFileRole.SpawnableTypes), Loaded("@CF"));

        IReadOnlyList<ConfiguredTypeFile> files = CreateService(fs).GetConfiguredFiles(config, MapName, "@CF");

        ConfiguredTypeFile file = Assert.Single(files);
        Assert.Equal("weird.xml", file.SourceRelative);
        Assert.Equal("CF_weird.xml", file.GeneratedLeaf);
        Assert.Equal(TypesFileRole.SpawnableTypes, file.Role);
    }

    [Fact]
    public void ConfigureMod_DuplicateGeneratedName_FailsWithoutCopying()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile($@"{WorkshopPath}\@CF\a\b.xml", "<b/>");
        fs.AddFile($@"{WorkshopPath}\@CF\a_b.xml", "<ab/>");
        var config = new TypesConfig();

        // Both flatten to CF_a_b.xml; the duplicate is rejected rather than
        // letting one copy silently overwrite the other.
        TypesOperationResult result = CreateService(fs).ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            Select($@"{WorkshopPath}\@CF\a\b.xml", $@"{WorkshopPath}\@CF\a_b.xml"), Loaded("@CF"));

        Assert.False(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("same generated file"));
        Assert.Empty(config.Maps[MapName].Mods);
        Assert.Null(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\CF_a_b.xml"));
    }

    [Fact]
    public void RemoveFiles_PrunesSourceAndRoleAlongsideGenerated()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile($@"{WorkshopPath}\@CF\weird.xml", "<weird/>");
        var config = new TypesConfig();
        TypesService service = CreateService(fs);

        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            new[]
            {
                new TypeFileSelection($@"{WorkshopPath}\@CF\types.xml", TypesFileRole.Types),
                new TypeFileSelection($@"{WorkshopPath}\@CF\weird.xml", TypesFileRole.SpawnableTypes),
            }, Loaded("@CF"));

        TypesOperationResult result = service.RemoveFiles(config, MapName, MissionPath, "@CF",
            new HashSet<string> { "CF_weird.xml" }, Loaded("@CF"));

        Assert.True(result.Success);
        ModTypesEntry entry = config.Maps[MapName].Mods.Single();
        Assert.Equal(new[] { "types.xml" }, entry.SourceFiles);
        Assert.Equal(new[] { @"db\ModTypes\CF_types.xml" }, entry.GeneratedFiles);
        Assert.Contains("CF_types.xml", entry.FileRoles.Keys);
        Assert.DoesNotContain("CF_weird.xml", entry.FileRoles.Keys);
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
            Select($@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF"));

        Assert.False(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("cfgeconomycore.xml"));
        Assert.False(fs.FileExists($@"{MissionPath}\db\ModTypes\CF_types.xml"));
        Assert.Empty(config.Maps[MapName].Mods);
    }

    [Fact]
    public void ConfigureMod_EconomyFailure_KeepsPreviouslyOwnedOverwrittenFile()
    {
        FakeFileSystem fs = Seed();
        var config = new TypesConfig();
        TypesService service = CreateService(fs);

        // First configuration succeeds and owns CF_types.xml.
        TypesOperationResult first = service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            Select($@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF"));
        Assert.True(first.Success);

        // The economy file becomes unwritable: reconfiguring overwrites the
        // existing (still-owned) CF_types.xml and then fails the economy rewrite.
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", "<economycore>");
        TypesOperationResult result = service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            Select($@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF"));

        Assert.False(result.Success);
        // The rollback must not delete a file that existed before the failed
        // attempt; the restored entry still references it.
        Assert.True(fs.FileExists($@"{MissionPath}\db\ModTypes\CF_types.xml"));
        Assert.Contains(config.Maps[MapName].Mods.Single().GeneratedFiles, f => f.EndsWith("CF_types.xml"));
    }

    [Fact]
    public void RemoveFiles_EconomyUnreadable_FailsWithoutDeletingFilesOrConfig()
    {
        FakeFileSystem fs = Seed();
        var config = new TypesConfig();
        var service = CreateService(fs);
        service.ConfigureMod(config, MapName, MissionPath, WorkshopPath, "@CF",
            Select($@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF"));

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
            Select($@"{WorkshopPath}\@CF\types.xml"), Loaded("@CF"));

        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", "<economycore>");
        TypesOperationResult result = service.CleanInvalid(config, MapName, MissionPath,
            new HashSet<string> { "@OtherMod" }, Loaded("@CF"));

        Assert.False(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("cfgeconomycore.xml"));
        Assert.NotNull(fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\CF_types.xml"));
        Assert.Single(config.Maps[MapName].Mods);
    }
}
