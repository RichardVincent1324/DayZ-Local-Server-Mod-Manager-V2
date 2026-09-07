using System.IO;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Tests.Models;

public class SettingsTests
{
    [Fact]
    public void BatFilePath_UsesBareName_RelativeToServerPath()
    {
        var settings = new Settings { ServerPath = @"D:\DayZServer", BatFileName = "run.bat" };

        Assert.Equal(Path.Combine(@"D:\DayZServer", "run.bat"), settings.BatFilePath);
    }
	
	[Fact]
    public void BatFilePath_UsesRootedPath_AsIs()
    {
        string rooted = Path.GetFullPath(Path.Combine("subdir", "run.bat"));
        var settings = new Settings { ServerPath = @"D:\DayZServer", BatFileName = rooted };

        Assert.Equal(rooted, settings.BatFilePath);
    }
}
