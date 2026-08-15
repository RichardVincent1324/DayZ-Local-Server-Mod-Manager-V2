using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class ServerConfigServiceTests
{
    private const string ServerPath = @"D:\DayZServer";

    [Fact]
    public void UpdateTemplate_ReplacesTemplateLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", """
            class Missions
            {
                class DayZ
                {
                    template="dayzOffline.chernarusplus"; // Mission to load on server startup.
                };
            };
            """);
        var service = new ServerConfigService(fs);

        bool result = service.UpdateTemplate(ServerPath, "dayzOffline.sakhal");

        Assert.True(result);
        string content = fs.TryGetFileContents($@"{ServerPath}\serverDZ.cfg")!;
        Assert.Contains("template=\"dayzOffline.sakhal\"", content);
        Assert.DoesNotContain("dayzOffline.chernarusplus", content);
        Assert.Contains("// Mission to load on server startup.", content);
    }

    [Fact]
    public void UpdateTemplate_ReturnsFalse_WhenNoTemplateLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", "hostname = \"x\";");
        var service = new ServerConfigService(fs);

        Assert.False(service.UpdateTemplate(ServerPath, "dayzOffline.sakhal"));
    }

    [Fact]
    public void UpdateTemplate_ReturnsFalse_WhenFileMissing()
    {
        var service = new ServerConfigService(new FakeFileSystem());

        Assert.False(service.UpdateTemplate(ServerPath, "dayzOffline.sakhal"));
    }
}
