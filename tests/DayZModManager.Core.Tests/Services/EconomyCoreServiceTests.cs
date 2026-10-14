using System.Xml.Linq;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class EconomyCoreServiceTests
{
    private const string MissionPath = @"D:\DayZServer\mpmissions\dayzOffline.chernarusplus";

    private const string Marker = "dzmmv2-managed-by=\"DayZ-Mod-Manager-V2\"";

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

    private static IReadOnlyDictionary<string, string> Types(params string[] names)
    {
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            types[name] = name.Contains("spawnable", StringComparison.OrdinalIgnoreCase) ? "spawnabletypes" : "types";
        }

        return types;
    }

    private static IReadOnlySet<string> Set(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    private static XDocument Parse(string xml) => XDocument.Parse(xml);

    private static XElement? FindManaged(XDocument doc) =>
        doc.Descendants("ce").FirstOrDefault(c => (string?)c.Attribute("dzmmv2-managed-by") == "DayZ-Mod-Manager-V2");

    private static List<string?> FileNames(XElement ce) =>
        ce.Elements("file").Select(f => (string?)f.Attribute("name")).ToList();

    private static EconomyCoreService CreateService(FakeFileSystem fs) => new(fs);

    [Fact]
    public void UpdateTypeFiles_CreatesManagedBlock_BeforeClasses()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);
        var service = CreateService(fs);

        EconomyCoreUpdateResult result = service.UpdateTypeFiles(
            MissionPath, new[] { "CF_types.xml", "Expansion_spawnabletypes.xml" },
            Types("CF_types.xml", "Expansion_spawnabletypes.xml"));

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);

        XElement? ce = FindManaged(doc);
        Assert.NotNull(ce);
        Assert.Equal("./db/type_files", (string?)ce!.Attribute("folder"));

        var files = ce.Elements("file").ToList();
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
        var service = CreateService(fs);
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["CF_types.xml"] = "spawnabletypes" };

        service.UpdateTypeFiles(MissionPath, new[] { "CF_types.xml" }, types);

        XElement file = FindManaged(Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!))!
            .Elements("file").Single();
        Assert.Equal("spawnabletypes", (string?)file.Attribute("type"));
    }

    [Fact]
    public void UpdateTypeFiles_FullyOverwritesManagedBlock_AndRewritesFolder()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="../../old/type_files" {Marker}>
            		<file name="OldMod_types.xml" type="types" />
            		<file name="CF_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = CreateService(fs);

        service.UpdateTypeFiles(MissionPath, new[] { "CF_types.xml" }, Types("CF_types.xml"));

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement ce = FindManaged(doc)!;
        Assert.Single(doc.Descendants("ce"));
        Assert.Equal("./db/type_files", (string?)ce.Attribute("folder"));
        Assert.Equal(new[] { "CF_types.xml" }, FileNames(ce));
    }

    [Fact]
    public void UpdateTypeFiles_EmptyDesired_KeepsEmptyManagedBlock_AndPreservesBase()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files">
            		<file name="types.xml" type="types" />
            	</ce>
            	<ce folder="../../DayZ-Mod-Manager-V2/Presets/m/p/type_files" {Marker}>
            		<file name="CF_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = CreateService(fs);

        service.UpdateTypeFiles(MissionPath, "../../DayZ-Mod-Manager-V2/Presets/m/p/type_files", Array.Empty<string>(), Types());

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement ce = FindManaged(doc)!;
        Assert.Empty(ce.Elements("file"));
        Assert.Equal("../../DayZ-Mod-Manager-V2/Presets/m/p/type_files", (string?)ce.Attribute("folder"));

        // The base block is untouched.
        XElement baseCe = doc.Descendants("ce").Single(c => (string?)c.Attribute("folder") == "./db/type_files");
        Assert.Equal(new[] { "types.xml" }, FileNames(baseCe));
    }

    [Fact]
    public void UpdateTypeFiles_UnmanagedLegacyBlock_LeftUntouched_AndWarns()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="../../DayZ-Mod-Manager-V2/Presets/m/p/type_files">
            		<file name="Legacy_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = CreateService(fs);

        EconomyCoreUpdateResult result = service.UpdateTypeFiles(
            MissionPath, "../../DayZ-Mod-Manager-V2/Presets/m/p/type_files", new[] { "CF_types.xml" }, Types("CF_types.xml"));

        Assert.True(result.Success);
        Assert.NotEmpty(result.Warnings);
        Assert.True(result.UnmanagedLegacyBlockDetected);

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Equal(2, doc.Descendants("ce").Count());

        // The legacy block is untouched.
        XElement legacy = doc.Descendants("ce").Single(c => (string?)c.Attribute("dzmmv2-managed-by") is null);
        Assert.Equal(new[] { "Legacy_types.xml" }, FileNames(legacy));

        // The new managed block carries the desired files.
        Assert.Equal(new[] { "CF_types.xml" }, FileNames(FindManaged(doc)!));
    }

    [Fact]
    public void UpdateTypeFiles_CreatesMarkerThenDoesNotFlagAgain()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="../../DayZ-Mod-Manager-V2/Presets/m/p/type_files">
            		<file name="Legacy_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = CreateService(fs);
        const string folder = "../../DayZ-Mod-Manager-V2/Presets/m/p/type_files";

        // First sync creates the managed block and reports the legacy block.
        EconomyCoreUpdateResult first = service.UpdateTypeFiles(
            MissionPath, folder, new[] { "CF_types.xml" }, Types("CF_types.xml"));
        Assert.True(first.UnmanagedLegacyBlockDetected);

        // The marker now exists, so a later sync does not report it again.
        EconomyCoreUpdateResult second = service.UpdateTypeFiles(
            MissionPath, folder, new[] { "CF_types.xml" }, Types("CF_types.xml"));
        Assert.False(second.UnmanagedLegacyBlockDetected);
    }

    [Fact]
    public void UpdateTypeFiles_BaseBlock_DoesNotWarn_AndIsPreserved()
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
        var service = CreateService(fs);

        EconomyCoreUpdateResult result = service.UpdateTypeFiles(
            MissionPath, "../../DayZ-Mod-Manager-V2/Presets/m/p/type_files", new[] { "CF_types.xml" }, Types("CF_types.xml"));

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
        Assert.False(result.UnmanagedLegacyBlockDetected);

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement baseCe = doc.Descendants("ce").Single(c => (string?)c.Attribute("dzmmv2-managed-by") is null);
        Assert.Equal(new[] { "types.xml" }, FileNames(baseCe));
        Assert.Equal(new[] { "CF_types.xml" }, FileNames(FindManaged(doc)!));
    }

    [Fact]
    public void UpdateTypeFiles_PreservesDesiredOrder()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);
        var service = CreateService(fs);

        var desired = new[] { "Zeta_types.xml", "Alpha_spawnabletypes.xml", "Beta_types.xml" };
        service.UpdateTypeFiles(MissionPath, desired, Types(desired));

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Equal(desired, FileNames(FindManaged(doc)!));
    }

    [Fact]
    public void UpdateTypeFiles_CorrectsTypeOnManagedEntry()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files" {Marker}>
            		<file name="SpawnableItems_types.xml" type="spawnabletypes" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = CreateService(fs);
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["SpawnableItems_types.xml"] = "types" };

        service.UpdateTypeFiles(MissionPath, new[] { "SpawnableItems_types.xml" }, types);

        XElement file = FindManaged(Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!))!
            .Elements("file").Single();
        Assert.Equal("types", (string?)file.Attribute("type"));
    }

    [Fact]
    public void UpdateTypeFiles_DoesNotRewrite_WhenAlreadyCorrect()
    {
        var fs = new FakeFileSystem();
        string original = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files" {Marker}>
            		<file name="CF_types.xml" type="types" />
            		<file name="CF_spawnabletypes.xml" type="spawnabletypes" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """;
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", original);
        var service = CreateService(fs);

        service.UpdateTypeFiles(
            MissionPath, new[] { "CF_types.xml", "CF_spawnabletypes.xml" },
            Types("CF_types.xml", "CF_spawnabletypes.xml"));

        // No rewrite happened: the file content is byte-for-byte unchanged.
        Assert.Equal(original, fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml"));
    }

    [Fact]
    public void UpdateTypeFiles_SwitchesManagedFolder_WithoutDuplicating()
    {
        var fs = new FakeFileSystem();
        const string saveFolder = "../../DayZ-Mod-Manager-V2/Progress_Saves/m/p/Alpha/type_files";
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="{saveFolder}" {Marker}>
            		<file name="saved_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = CreateService(fs);

        service.UpdateTypeFiles(MissionPath, new[] { "CF_types.xml" }, Types("CF_types.xml"));

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement ce = doc.Descendants("ce").Single();
        Assert.Equal("./db/type_files", (string?)ce.Attribute("folder"));
        Assert.Equal(new[] { "CF_types.xml" }, FileNames(ce));
    }

    [Fact]
    public void UpdateTypeFiles_RepeatedCalls_DoNotDuplicateBlock()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);
        var service = CreateService(fs);

        service.UpdateTypeFiles(MissionPath, new[] { "CF_types.xml" }, Types("CF_types.xml"));
        service.UpdateTypeFiles(MissionPath, new[] { "CF_types.xml" }, Types("CF_types.xml"));

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Single(doc.Descendants("ce"));
    }

    [Fact]
    public void UpdateTypeFiles_ReturnsFalse_WhenFileMissing()
    {
        var service = CreateService(new FakeFileSystem());

        Assert.False(service.UpdateTypeFiles(MissionPath, new[] { "x.xml" }, Types("x.xml")).Success);
    }

    [Fact]
    public void UpdateTypeFiles_ReturnsFalse_WhenMalformed()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", "<economycore>");
        var service = CreateService(fs);

        Assert.False(service.UpdateTypeFiles(MissionPath, new[] { "x.xml" }, Types("x.xml")).Success);
    }

    [Fact]
    public void RemoveTypeFiles_RemovesOnlyFromManagedBlock_KeepsUnmanaged()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files">
            		<file name="Orphan_types.xml" type="types" />
            	</ce>
            	<ce folder="../../DayZ-Mod-Manager-V2/Presets/m/p/type_files" {Marker}>
            		<file name="Orphan_types.xml" type="types" />
            		<file name="CF_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = CreateService(fs);

        bool result = service.RemoveTypeFiles(MissionPath, Set("Orphan_types.xml"));

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Equal(new[] { "CF_types.xml" }, FileNames(FindManaged(doc)!));

        XElement unmanaged = doc.Descendants("ce").Single(c => (string?)c.Attribute("dzmmv2-managed-by") is null);
        Assert.Equal(new[] { "Orphan_types.xml" }, FileNames(unmanaged));
    }

    [Fact]
    public void RemoveTypeFiles_KeepsManagedBlock_WhenItEmpties()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files" {Marker}>
            		<file name="Orphan_types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = CreateService(fs);

        Assert.True(service.RemoveTypeFiles(MissionPath, Set("Orphan_types.xml")));

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement ce = FindManaged(doc)!;
        Assert.Empty(ce.Elements("file"));
        Assert.Contains(doc.Descendants("rootclass"), r => (string?)r.Attribute("name") == "DefaultWeapon");
    }

    [Fact]
    public void RemoveTypeFiles_ReturnsTrue_WhenNothingNamedPresent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);

        Assert.True(new EconomyCoreService(fs).RemoveTypeFiles(MissionPath, Set("Ghost_types.xml")));
    }

    [Fact]
    public void RemoveTypeFiles_ReturnsFalse_WhenFileMissing()
    {
        var service = CreateService(new FakeFileSystem());

        Assert.False(service.RemoveTypeFiles(MissionPath, Set("x.xml")));
    }

    [Fact]
    public void RemoveTypeFiles_ReturnsFalse_WhenMalformed()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", "<economycore>");

        Assert.False(new EconomyCoreService(fs).RemoveTypeFiles(MissionPath, Set("x.xml")));
    }

    [Fact]
    public void GetTypeFilesFolder_ReturnsManagedBlockFolder()
    {
        var fs = new FakeFileSystem();
        const string saveFolder = "../../DayZ-Mod-Manager-V2/Progress_Saves/m/p/Alpha/type_files";
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="{saveFolder}" {Marker}><file name="x.xml" type="types" /></ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);

        Assert.Equal(saveFolder, new EconomyCoreService(fs).GetTypeFilesFolder(MissionPath));
    }

    [Fact]
    public void GetTypeFilesFolder_ReturnsNull_WhenOnlyUnmanagedBlocks()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/type_files"><file name="types.xml" type="types" /></ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);

        Assert.Null(new EconomyCoreService(fs).GetTypeFilesFolder(MissionPath));
    }
}
