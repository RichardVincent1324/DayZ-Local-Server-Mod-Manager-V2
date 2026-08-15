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

    private static XDocument Parse(string xml) => XDocument.Parse(xml);

    [Fact]
    public void UpdateModTypes_InsertsCeBlockBeforeClasses()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", ConfigXml);
        var service = new EconomyCoreService(fs);

        bool result = service.UpdateModTypes(MissionPath, new[] { "CF_types.xml", "Expansion_spawnabletypes.xml" });

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);

        XElement? ce = doc.Descendants("ce").FirstOrDefault(c => (string?)c.Attribute("folder") == "./db/ModTypes");
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
    public void UpdateModTypes_RemovesOldFormats_WhenInserting()
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

        service.UpdateModTypes(MissionPath, new[] { "CF_types.xml" });

        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.DoesNotContain(doc.Descendants("file"), f => (string?)f.Attribute("name") == "old.xml");
        Assert.Single(doc.Descendants("ce"));
    }

    [Fact]
    public void UpdateModTypes_RemovesBlock_WhenEmptyList()
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

        bool result = service.UpdateModTypes(MissionPath, Array.Empty<string>());

        Assert.True(result);
        XDocument doc = Parse(fs.TryGetFileContents($@"{MissionPath}\cfgeconomycore.xml")!);
        Assert.Empty(doc.Descendants("ce"));
    }

    [Fact]
    public void UpdateModTypes_ReturnsFalse_WhenFileMissing()
    {
        var service = new EconomyCoreService(new FakeFileSystem());

        Assert.False(service.UpdateModTypes(MissionPath, new[] { "x.xml" }));
    }

    [Fact]
    public void UpdateModTypes_ReturnsFalse_WhenMalformed()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{MissionPath}\cfgeconomycore.xml", "<economycore>");
        var service = new EconomyCoreService(fs);

        Assert.False(service.UpdateModTypes(MissionPath, new[] { "x.xml" }));
    }
}
