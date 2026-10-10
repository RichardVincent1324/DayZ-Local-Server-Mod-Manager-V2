using System.Xml.Linq;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class EconomyCoreServiceTests
{
    private const string MissionPath = @"D:\DayZServer\mpmissions\dayzOffline.chernarusplus";

    private const string ConfigXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
        <economycore>
        	<classes>
        		<rootclass name="DefaultWeapon" />
        	</classes>
        	<defaults>
        		<default name="dyn_radius" value="30" />
        	</defaults>
        </economycore>
        """;

    private static IReadOnlySet<string> Set(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Empty() => Set();

    private static IReadOnlyDictionary<string, string> Types(params string[] names)
    {
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            types[name] = name.Contains("spawnable", StringComparison.OrdinalIgnoreCase) ? "spawnabletypes" : "types";
        }

        return types;
    }

    private static XDocument Parse(string xml) => XDocument.Parse(xml);

    private static XElement? FindCe(XDocument doc) =>
        doc.Descendants("ce").FirstOrDefault(c => (string?)c.Attribute("folder") == "./db/type_files");

    [Fact]
    public void UpdateTypeFiles_InsertsCeBlockBeforeClasses()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);
        var service = new EconomyCoreService(fs);

        bool result = service.UpdateTypeFiles(MissionPath, new[] { "CF_types.xml", "Expansion_spawnabletypes.xml" }, Empty(),
            Types("CF_types.xml", "Expansion_spawnabletypes.xml"));

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);

        XElement? ce = FindCe(doc);
        Assert.NotNull(ce);

        var files = ce!.Elements("file").ToList();
        Assert.Equal(2, files.Count);
        Assert.Equal("CF_types.xml", (string?)files[0].Attribute("name"));
        Assert.Equal("types", (string?)files[0].Attribute("type"));
        Assert.Equal("Expansion_spawnabletypes.xml", (string?)files[1].Attribute("name"));
        Assert.Equal("spawnabletypes", (string?)files[1].Attribute("type"));

        // Existing content preserved
        Assert.Contains(doc.Descendants("rootclass"), r => (string?)r.Attribute("name") == "DefaultWeapon");
        Assert.Contains(doc.Descendants("default"), d => (string?)d.Attribute("name") == "dyn_radius");
    }

    [Fact]
    public void UpdateTypeFiles_ExplicitFileTypes_AreUsed()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);
        var service = new EconomyCoreService(fs);
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CF_types.xml"] = "spawnabletypes",
        };

        bool result = service.UpdateTypeFiles(MissionPath, new[] { "CF_types.xml" }, Empty(), types);

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement file = FindCe(doc)!.Elements("file").Single();
        Assert.Equal("spawnabletypes", (string?)file.Attribute("type"));
    }

    [Fact]
    public void UpdateTypeFiles_CorrectsTypeOnExistingEntry()
    {
        // The entry is present in the right order but carries a stale type; the
        // explicit type must still trigger a rewrite.
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files">
            		<file name="SpawnableItems_types.xml" type="spawnabletypes" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SpawnableItems_types.xml"] = "types",
        };

        bool result = service.UpdateTypeFiles(MissionPath, new[] { "SpawnableItems_types.xml" }, Empty(), types);

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement file = FindCe(doc)!.Elements("file").Single();
        Assert.Equal("types", (string?)file.Attribute("type"));
    }

    [Fact]
    public void UpdateTypeFiles_RemovesOnlyOwnedStaleEntries_KeepsBaseAndThirdParty()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files">
            		<file name="types.xml" type="types" />
            		<file name="cfgspawnabletypes.xml" type="spawnabletypes" />
            		<file name="CF_types.xml" type="types" />
            		<file name="OldMod_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        // OldMod is no longer desired and is app-owned -> removed; base + CF kept.
        bool result = service.UpdateTypeFiles(
            MissionPath,
            new[] { "CF_types.xml" },
            Set("CF_types.xml", "OldMod_types.xml"),
            Types("CF_types.xml"));

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement? ce = FindCe(doc);
        Assert.NotNull(ce);

        var names = ce!.Elements("file").Select(f => (string?)f.Attribute("name")).ToList();
        Assert.Contains("types.xml", names);
        Assert.Contains("cfgspawnabletypes.xml", names);
        Assert.Contains("CF_types.xml", names);
        Assert.DoesNotContain("OldMod_types.xml", names);
    }

    [Fact]
    public void UpdateTypeFiles_EmptyDesired_PreservesNonOwnedEntries()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files">
            		<file name="types.xml" type="types" />
            		<file name="cfgspawnabletypes.xml" type="spawnabletypes" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        bool result = service.UpdateTypeFiles(MissionPath, Array.Empty<string>(), Empty(), Types());

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement? ce = FindCe(doc);
        Assert.NotNull(ce);
        Assert.Equal(2, ce!.Elements("file").Count());
    }

    [Fact]
    public void UpdateTypeFiles_EmptyDesired_RemovesOwnedStaleEntries()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files"><file name="old.xml" type="types" /></ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        bool result = service.UpdateTypeFiles(MissionPath, Array.Empty<string>(), Set("old.xml"), Types());

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Empty(doc.Descendants("ce"));
    }

    [Fact]
    public void UpdateTypeFiles_MergesIntoExistingBlock_WithoutDuplicating()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files">
            		<file name="types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        service.UpdateTypeFiles(MissionPath, new[] { "CF_types.xml" }, Set("CF_types.xml"), Types("CF_types.xml"));
        service.UpdateTypeFiles(MissionPath, new[] { "CF_types.xml" }, Set("CF_types.xml"), Types("CF_types.xml"));

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Single(doc.Descendants("ce"));
        XElement? ce = FindCe(doc);
        Assert.Equal(2, ce!.Elements("file").Count());
    }

    [Fact]
    public void UpdateTypeFiles_MatchesForwardSlashFolderForm_WithoutDuplicating()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="db/type_files"><file name="types.xml" type="types" /></ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        service.UpdateTypeFiles(MissionPath, new[] { "CF_types.xml" }, Set("CF_types.xml"), Types("CF_types.xml"));

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Single(doc.Descendants("ce"));
        var names = doc.Descendants("file").Select(f => (string?)f.Attribute("name")).ToList();
        Assert.Contains("types.xml", names);
        Assert.Contains("CF_types.xml", names);
    }

    [Fact]
    public void UpdateTypeFiles_PreservesDesiredOrder()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);
        var service = new EconomyCoreService(fs);

        // Deliberately non-alphabetical: the caller's order must be preserved.
        var desired = new[] { "Zeta_types.xml", "Alpha_spawnabletypes.xml", "Beta_types.xml" };
        bool result = service.UpdateTypeFiles(MissionPath, desired, Empty(), Types(desired));

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement? ce = FindCe(doc);
        Assert.NotNull(ce);

        var names = ce!.Elements("file").Select(f => (string?)f.Attribute("name")).ToList();
        Assert.Equal(new[] { "Zeta_types.xml", "Alpha_spawnabletypes.xml", "Beta_types.xml" }, names);
    }

    [Fact]
    public void UpdateTypeFiles_OrdersTypesBeforeSpawnable_WhenAddingToExistingBlock()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files">
            		<file name="Mod_spawnabletypes.xml" type="spawnabletypes" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        bool result = service.UpdateTypeFiles(
            MissionPath, new[] { "Mod_types.xml", "Mod_spawnabletypes.xml" }, Empty(),
            Types("Mod_types.xml", "Mod_spawnabletypes.xml"));

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement? ce = FindCe(doc);
        Assert.NotNull(ce);

        var names = ce!.Elements("file").Select(f => (string?)f.Attribute("name")).ToList();
        Assert.Equal(new[] { "Mod_types.xml", "Mod_spawnabletypes.xml" }, names);
    }

    [Fact]
    public void UpdateTypeFiles_ReordersExistingDesiredEntries_ToRequestedOrder()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files">
            		<file name="CF_spawnabletypes.xml" type="spawnabletypes" />
            		<file name="CF_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        bool result = service.UpdateTypeFiles(
            MissionPath, new[] { "CF_types.xml", "CF_spawnabletypes.xml" }, Empty(),
            Types("CF_types.xml", "CF_spawnabletypes.xml"));

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement? ce = FindCe(doc);
        Assert.NotNull(ce);

        var names = ce!.Elements("file").Select(f => (string?)f.Attribute("name")).ToList();
        Assert.Equal(new[] { "CF_types.xml", "CF_spawnabletypes.xml" }, names);
    }

    [Fact]
    public void UpdateTypeFiles_DoesNotRewrite_WhenAlreadyInOrder()
    {
        var fs = new FakeFileSystem();
        string original = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files">
            		<file name="CF_types.xml" type="types" />
            		<file name="CF_spawnabletypes.xml" type="spawnabletypes" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """;
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", original);
        var service = new EconomyCoreService(fs);

        bool result = service.UpdateTypeFiles(
            MissionPath, new[] { "CF_types.xml", "CF_spawnabletypes.xml" }, Empty(),
            Types("CF_types.xml", "CF_spawnabletypes.xml"));

        Assert.True(result);
        // No rewrite happened: the file content is byte-for-byte unchanged.
        Assert.Equal(original, fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml"));
    }

    [Fact]
    public void UpdateTypeFiles_ReturnsFalse_WhenFileMissing()
    {
        var service = new EconomyCoreService(new FakeFileSystem());

        Assert.False(service.UpdateTypeFiles(MissionPath, new[] { "x.xml" }, Empty(), Types("x.xml")));
    }

    [Fact]
    public void UpdateTypeFiles_ReturnsFalse_WhenMalformed()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", "<economycore>");
        var service = new EconomyCoreService(fs);

        Assert.False(service.UpdateTypeFiles(MissionPath, new[] { "x.xml" }, Empty(), Types("x.xml")));
    }

    [Fact]
    public void RemoveTypeFiles_RemovesOnlyTheNamedEntries()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files">
            		<file name="Orphan_types.xml" type="types" />
            		<file name="CF_types.xml" type="types" />
            		<file name="Other_orphan_spawnabletypes.xml" type="spawnabletypes" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        bool result = service.RemoveTypeFiles(MissionPath, Set("Orphan_types.xml"));

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement? ce = FindCe(doc);
        Assert.NotNull(ce);
        var names = ce!.Elements("file").Select(f => (string?)f.Attribute("name")).ToList();
        Assert.Equal(new[] { "CF_types.xml", "Other_orphan_spawnabletypes.xml" }, names);
    }

    [Fact]
    public void RemoveTypeFiles_DropsBlock_WhenItEmpties()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files">
            		<file name="Orphan_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        bool result = service.RemoveTypeFiles(MissionPath, Set("Orphan_types.xml"));

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.DoesNotContain(doc.Descendants("file"), f => (string?)f.Attribute("name") == "Orphan_types.xml");
        Assert.DoesNotContain(doc.Descendants("ce"), c => (string?)c.Attribute("folder") == "./db/type_files");
        Assert.Contains(doc.Descendants("rootclass"), r => (string?)r.Attribute("name") == "DefaultWeapon");
    }

    [Fact]
    public void RemoveTypeFiles_ReturnsTrue_WhenNothingNamedPresent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);
        var service = new EconomyCoreService(fs);

        Assert.True(service.RemoveTypeFiles(MissionPath, Set("Ghost_types.xml")));
    }

    [Fact]
    public void RemoveTypeFiles_ReturnsFalse_WhenFileMissing()
    {
        var service = new EconomyCoreService(new FakeFileSystem());

        Assert.False(service.RemoveTypeFiles(MissionPath, Set("x.xml")));
    }

    [Fact]
    public void RemoveTypeFiles_ReturnsFalse_WhenMalformed()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", "<economycore>");
        var service = new EconomyCoreService(fs);

        Assert.False(service.RemoveTypeFiles(MissionPath, Set("x.xml")));
    }

    [Fact]
    public void UpdateTypeFiles_WritesRequestedFolderValue()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);
        var service = new EconomyCoreService(fs);
        const string saveFolder = "../../DayZ-Mod-Manager-V2/Progress_Saves/dayzOffline.chernarusplus/Alpha/type_files";

        bool result = service.UpdateTypeFiles(MissionPath, saveFolder, new[] { "CF_types.xml" }, Empty(), Types("CF_types.xml"));

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Equal(saveFolder, (string?)doc.Descendants("ce").Single().Attribute("folder"));
    }

    [Fact]
    public void UpdateTypeFiles_SwitchesAnExistingSavePathBlockBackToConfigured_WithoutDuplicating()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="../../DayZ-Mod-Manager-V2/Progress_Saves/dayzOffline.chernarusplus/Alpha/type_files">
            		<file name="saved_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        bool result = service.UpdateTypeFiles(MissionPath, new[] { "CF_types.xml" }, Set("saved_types.xml"), Types("CF_types.xml"));

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement ce = doc.Descendants("ce").Single();
        Assert.Equal("./db/type_files", (string?)ce.Attribute("folder"));
        Assert.Equal(new[] { "CF_types.xml" }, ce.Elements("file").Select(f => (string?)f.Attribute("name")).ToList());
    }

    [Fact]
    public void GetTypeFilesFolder_ReturnsTheManagerBlockFolder()
    {
        var fs = new FakeFileSystem();
        const string saveFolder = "../../DayZ-Mod-Manager-V2/Progress_Saves/dayzOffline.chernarusplus/Alpha/type_files";
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="{saveFolder}"><file name="x.xml" type="types" /></ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        Assert.Equal(saveFolder, service.GetTypeFilesFolder(MissionPath));
    }

    [Fact]
    public void GetTypeFilesFolder_ReturnsNull_WhenNoManagerBlock()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);

        Assert.Null(new EconomyCoreService(fs).GetTypeFilesFolder(MissionPath));
    }
}
