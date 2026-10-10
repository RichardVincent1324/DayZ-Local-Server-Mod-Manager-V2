using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class ServerConfigServiceTests
{
    private const string ServerPath = @"D:\DayZServer";

    private static string ConfigPath => $@"{ServerPath}\serverDZ.cfg";

    [Fact]
    public void UpdateTemplate_ReplacesTemplateLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(ConfigPath, """
            class Missions
            {
                class DayZ
                {
                    template="dayzOffline.chernarusplus"; // Mission to load on server startup.
                };
            };
            """);
        var service = new ServerConfigService(fs);

        bool result = service.UpdateTemplate(ConfigPath, "dayzOffline.sakhal");

        Assert.True(result);
        string content = fs.TryGetFileContents(ConfigPath)!;
        Assert.Contains("template=\"dayzOffline.sakhal\"", content);
        Assert.DoesNotContain("dayzOffline.chernarusplus", content);
        Assert.Contains("// Mission to load on server startup.", content);
    }

    [Fact]
    public void UpdateTemplate_IgnoresCommentedTemplateLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(ConfigPath, """
            class Missions
            {
                class DayZ
                {
                    ;template="dayzOffline.chernarusplus"; // sample comment
                    template="dayzOffline.chernarusplus"; // Mission to load on server startup.
                };
            };
            """);
        var service = new ServerConfigService(fs);

        bool result = service.UpdateTemplate(ConfigPath, "dayzOffline.sakhal");

        Assert.True(result);
        string content = fs.TryGetFileContents(ConfigPath)!;
        Assert.Contains("template=\"dayzOffline.sakhal\"", content);
        Assert.Contains(";template=\"dayzOffline.chernarusplus\"", content);
    }

    [Fact]
    public void UpdateTemplate_ReturnsFalse_WhenNoTemplateLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(ConfigPath, "hostname = \"x\";");
        var service = new ServerConfigService(fs);

        Assert.False(service.UpdateTemplate(ConfigPath, "dayzOffline.sakhal"));
    }

    [Fact]
    public void UpdateTemplate_ReturnsFalse_WhenFileMissing()
    {
        var service = new ServerConfigService(new FakeFileSystem());

        Assert.False(service.UpdateTemplate(ConfigPath, "dayzOffline.sakhal"));
    }

    [Fact]
    public void WriteInstanceId_ReplacesExistingLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(ConfigPath, "instanceId=1;\nhostname=\"x\";");
        var service = new ServerConfigService(fs);

        Assert.True(service.WriteInstanceId(ConfigPath, 7));

        string content = fs.TryGetFileContents(ConfigPath)!;
        Assert.Contains("instanceId=7;", content);
        Assert.DoesNotContain("instanceId=1;", content);
    }

    [Fact]
    public void WriteInstanceId_AppendsWhenLineMissing()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(ConfigPath, "hostname=\"x\";\n");
        var service = new ServerConfigService(fs);

        Assert.True(service.WriteInstanceId(ConfigPath, 3));

        Assert.Contains("instanceId=3;", fs.TryGetFileContents(ConfigPath)!);
    }

    [Fact]
    public void WriteInstanceId_ReturnsFalse_WhenFileMissing()
    {
        var service = new ServerConfigService(new FakeFileSystem());

        Assert.False(service.WriteInstanceId(ConfigPath, 1));
    }

    [Fact]
    public void TryReadInstanceId_ReturnsValue()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(ConfigPath, "instanceId=7;\nhostname=\"x\";");
        var service = new ServerConfigService(fs);

        Assert.Equal(7, service.TryReadInstanceId(ConfigPath));
    }

    [Fact]
    public void TryReadInstanceId_ReturnsNull_WhenLineMissing()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(ConfigPath, "hostname=\"x\";");
        var service = new ServerConfigService(fs);

        Assert.Null(service.TryReadInstanceId(ConfigPath));
    }

    [Fact]
    public void TryReadInstanceId_ReturnsNull_WhenFileMissing()
    {
        var service = new ServerConfigService(new FakeFileSystem());

        Assert.Null(service.TryReadInstanceId(ConfigPath));
    }
}
