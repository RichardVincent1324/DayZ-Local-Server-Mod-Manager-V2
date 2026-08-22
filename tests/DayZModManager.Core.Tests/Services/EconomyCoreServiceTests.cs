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

    private static XDocument Parse(string xml) => XDocument.Parse(xml);

    private static XElement? FindCe(XDocument doc) =>
        doc.Descendants("ce").FirstOrDefault(c => (string?)c.Attribute("folder") == "./db/ModTypes");

    [Fact]
    public void UpdateModTypes_InsertsCeBlockBeforeClasses()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);
        var service = new EconomyCoreService(fs);

        bool result = service.UpdateModTypes(MissionPath, new[] { "CF_types.xml", "Expansion_spawnabletypes.xml" }, Empty());

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
    public void UpdateModTypes_RemovesOnlyOwnedStaleEntries_KeepsBaseAndThirdParty()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/ModTypes">
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
        bool result = service.UpdateModTypes(
            MissionPath,
            new[] { "CF_types.xml" },
            Set("CF_types.xml", "OldMod_types.xml"));

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
    public void UpdateModTypes_EmptyDesired_PreservesNonOwnedEntries()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/ModTypes">
            		<file name="types.xml" type="types" />
            		<file name="cfgspawnabletypes.xml" type="spawnabletypes" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        bool result = service.UpdateModTypes(MissionPath, Array.Empty<string>(), Empty());

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement? ce = FindCe(doc);
        Assert.NotNull(ce);
        Assert.Equal(2, ce!.Elements("file").Count());
    }

    [Fact]
    public void UpdateModTypes_EmptyDesired_RemovesOwnedStaleEntries()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/ModTypes"><file name="old.xml" type="types" /></ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        bool result = service.UpdateModTypes(MissionPath, Array.Empty<string>(), Set("old.xml"));

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Empty(doc.Descendants("ce"));
    }

    [Fact]
    public void UpdateModTypes_RemovesOwnedStale_FromLegacyFolderForm()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="ModTypes"><file name="old.xml" type="types" /></ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        service.UpdateModTypes(MissionPath, new[] { "CF_types.xml" }, Set("old.xml"));

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.DoesNotContain(doc.Descendants("file"), f => (string?)f.Attribute("name") == "old.xml");
        Assert.Single(doc.Descendants("ce"));
    }

    [Fact]
    public void UpdateModTypes_MergesIntoExistingBlock_WithoutDuplicating()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="./db/ModTypes">
            		<file name="types.xml" type="types" />
            	</ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        service.UpdateModTypes(MissionPath, new[] { "CF_types.xml" }, Set("CF_types.xml"));
        service.UpdateModTypes(MissionPath, new[] { "CF_types.xml" }, Set("CF_types.xml"));

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Single(doc.Descendants("ce"));
        XElement? ce = FindCe(doc);
        Assert.Equal(2, ce!.Elements("file").Count());
    }

    [Fact]
    public void UpdateModTypes_MatchesForwardSlashFolderForm_WithoutDuplicating()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <economycore>
            	<ce folder="db/ModTypes"><file name="types.xml" type="types" /></ce>
            	<classes><rootclass name="DefaultWeapon" /></classes>
            </economycore>
            """);
        var service = new EconomyCoreService(fs);

        service.UpdateModTypes(MissionPath, new[] { "CF_types.xml" }, Set("CF_types.xml"));

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Single(doc.Descendants("ce"));
        var names = doc.Descendants("file").Select(f => (string?)f.Attribute("name")).ToList();
        Assert.Contains("types.xml", names);
        Assert.Contains("CF_types.xml", names);
    }

    [Fact]
    public void UpdateModTypes_PreservesDesiredOrder()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);
        var service = new EconomyCoreService(fs);

        // Deliberately non-alphabetical: the caller's order must be preserved.
        var desired = new[] { "Zeta_types.xml", "Alpha_spawnabletypes.xml", "Beta_types.xml" };
        bool result = service.UpdateModTypes(MissionPath, desired, Empty());

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        XElement? ce = FindCe(doc);
        Assert.NotNull(ce);

        var names = ce!.Elements("file").Select(f => (string?)f.Attribute("name")).ToList();
        Assert.Equal(new[] { "Zeta_types.xml", "Alpha_spawnabletypes.xml", "Beta_types.xml" }, names);
    }

    [Fact]
    public void UpdateModTypes_ReturnsFalse_WhenFileMissing()
    {
        var service = new EconomyCoreService(new FakeFileSystem());

        Assert.False(service.UpdateModTypes(MissionPath, new[] { "x.xml" }, Empty()));
    }

    [Fact]
    public void UpdateModTypes_ReturnsFalse_WhenMalformed()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", "<economycore>");
        var service = new EconomyCoreService(fs);

        Assert.False(service.UpdateModTypes(MissionPath, new[] { "x.xml" }, Empty()));
    }
}
