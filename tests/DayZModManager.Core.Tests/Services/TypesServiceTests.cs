using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class TypesServiceTests
{
    private const string WorkshopPath = @"D:\DayZ\!Workshop";
    private const string MissionPath = @"D:\DayZServer\mpmissions\dayzOffline.chernarusplus";
    private const string PresetRoot = @"D:\app\data\Presets\dayzOffline.chernarusplus\__default_preset__";
    private const string TypeFilesFolder = PresetRoot + @"\type_files";

    private const string EconomyCoreXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
        <economycore>
        	<classes>
        		<rootClass name="DefaultWeapon" />
        	</classes>
        </economycore>
        """;

    private static TypesService CreateService(FakeFileSystem fs) =>
        new(fs, new EconomyCoreService(fs));

    private static Dictionary<string, string> Assignments(params (string Name, string Role)[] entries) =>
        entries.ToDictionary(entry => entry.Name, entry => entry.Role, StringComparer.OrdinalIgnoreCase);

    private static FakeFileSystem Seed()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory($@"{WorkshopPath}\@CF");
        fs.AddFile($@"{WorkshopPath}\@CF\types.xml", "<types/>");
        fs.AddFile($@"{WorkshopPath}\@CF\cfgspawnabletypes.xml", "<spawnabletypes/>");
        fs.AddFile($@"{WorkshopPath}\@CF\economy.xml", "<economy/>");
        fs.AddDirectory(TypeFilesFolder);
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", EconomyCoreXml);
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

    [Fact]
    public void ImportFiles_CopiesSelectedFilesDirectly()
    {
        FakeFileSystem fs = Seed();

        TypesOperationResult result = CreateService(fs).ImportFiles(
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, TypeFilesFolder, overwrite: false);

        Assert.True(result.Success);
        Assert.True(fs.FileExists($@"{TypeFilesFolder}\types.xml"));
        Assert.Equal("<types/>", fs.TryGetFileContents($@"{TypeFilesFolder}\types.xml"));
    }

    [Fact]
    public void ImportFiles_SkipsCollision_WhenNotOverwriting()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile($@"{TypeFilesFolder}\types.xml", "<existing/>");

        TypesOperationResult result = CreateService(fs).ImportFiles(
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, TypeFilesFolder, overwrite: false);

        Assert.True(result.Success);
        Assert.Equal("<existing/>", fs.TryGetFileContents($@"{TypeFilesFolder}\types.xml"));
        Assert.Contains(result.Messages, m => m.Contains("Skipped"));
    }

    [Fact]
    public void ImportFiles_OverwritesCollision_WhenConfirmed()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile($@"{TypeFilesFolder}\types.xml", "<existing/>");

        TypesOperationResult result = CreateService(fs).ImportFiles(
            new[] { $@"{WorkshopPath}\@CF\types.xml" }, TypeFilesFolder, overwrite: true);

        Assert.True(result.Success);
        Assert.Equal("<types/>", fs.TryGetFileContents($@"{TypeFilesFolder}\types.xml"));
    }

    [Fact]
    public void GetActiveTypeFiles_GroupsTypesBeforeSpawnable_AndNaturalSorts()
    {
        FakeFileSystem fs = Seed();
        foreach (string name in new[]
                 {
                     "99_override.xml", "10_weapons.xml", "00_base.xml", "custom_spawnable.xml",
                 })
        {
            fs.AddFile(Path.Combine(TypeFilesFolder, name), "<types/>");
        }

        IReadOnlyList<ManagedTypeFileEntry> files = CreateService(fs).GetActiveTypeFiles(
            TypeFilesFolder,
            Assignments(
                ("99_override.xml", "types"),
                ("10_weapons.xml", "types"),
                ("00_base.xml", "types"),
                ("custom_spawnable.xml", "spawnabletypes")));

        Assert.Equal(
            new[] { "00_base.xml", "10_weapons.xml", "99_override.xml", "custom_spawnable.xml" },
            files.Select(file => file.Name));
        Assert.Equal("spawnabletypes", files[^1].Type);
    }

    [Fact]
    public void GetActiveTypeFiles_ExcludesUnassignedFiles()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile(Path.Combine(TypeFilesFolder, "assigned.xml"), "<types/>");
        fs.AddFile(Path.Combine(TypeFilesFolder, "unassigned.xml"), "<types/>");

        IReadOnlyList<ManagedTypeFileEntry> files = CreateService(fs).GetActiveTypeFiles(
            TypeFilesFolder, Assignments(("assigned.xml", "types")));

        Assert.Single(files);
        Assert.Equal("assigned.xml", files[0].Name);
    }

    [Fact]
    public void SyncEconomyCore_UsesFreshDirectoryScan()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile(Path.Combine(TypeFilesFolder, "00_base.xml"), "<types/>");
        var service = CreateService(fs);
        // The assignment refers to a file that was added after the caller loaded state.
        var assignments = Assignments(("00_base.xml", "types"));

        TypesOperationResult result = service.SyncEconomyCore(
            MissionPath, EconomyCoreService.ManagedFolder, TypeFilesFolder, assignments);

        Assert.True(result.Success);
        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("00_base.xml", economy);
        Assert.Contains("type=\"types\"", economy);
        Assert.Contains("dzmmv2-managed-by", economy);
    }

    [Fact]
    public void SyncEconomyCore_ExcludesUnassignedAndWritesBothGroups()
    {
        FakeFileSystem fs = Seed();
        foreach (string name in new[] { "a_types.xml", "b_spawn.xml", "c_unassigned.xml" })
        {
            fs.AddFile(Path.Combine(TypeFilesFolder, name), "<types/>");
        }

        TypesOperationResult result = CreateService(fs).SyncEconomyCore(
            MissionPath,
            EconomyCoreService.ManagedFolder,
            TypeFilesFolder,
            Assignments(("a_types.xml", "types"), ("b_spawn.xml", "spawnabletypes")));

        Assert.True(result.Success);
        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        int typesIndex = economy.IndexOf("a_types.xml", StringComparison.Ordinal);
        int spawnIndex = economy.IndexOf("b_spawn.xml", StringComparison.Ordinal);
        Assert.True(typesIndex >= 0);
        Assert.True(spawnIndex >= 0);
        Assert.True(typesIndex < spawnIndex);
        Assert.DoesNotContain("c_unassigned.xml", economy);
    }

    [Fact]
    public void SyncEconomyCore_PropagatesUnmanagedV2CeBlockDetection()
    {
        FakeFileSystem fs = Seed();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/ModTypes">
            		<file name="Legacy_types.xml" type="types" />
            	</ce>
            	<classes><rootClass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        fs.AddFile(Path.Combine(TypeFilesFolder, "types.xml"), "<types/>");

        TypesOperationResult result = CreateService(fs).SyncEconomyCore(
            MissionPath,
            EconomyCoreService.ManagedFolder,
            TypeFilesFolder,
            Assignments(("types.xml", "types")));

        Assert.True(result.Success);
        Assert.True(result.UnmanagedV2CeBlockDetected);
    }

    [Fact]
    public void SyncEconomyCore_NoTypeFiles_WritesEmptyManagedBlock()
    {
        FakeFileSystem fs = Seed();

        TypesOperationResult result = CreateService(fs).SyncEconomyCore(
            MissionPath, EconomyCoreService.ManagedFolder, TypeFilesFolder, Assignments());

        Assert.True(result.Success);
        string economy = fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!;
        Assert.Contains("<ce", economy);
        Assert.DoesNotContain("<file", economy);
    }
}
