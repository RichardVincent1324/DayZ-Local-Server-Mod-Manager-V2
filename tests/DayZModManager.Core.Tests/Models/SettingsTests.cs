using System.IO;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Tests.Models;

public class SettingsTests
{
    [Fact]
    public void BatchFile_DefaultsToEmpty()
    {
        var settings = new Settings();

        Assert.Equal(string.Empty, settings.BatchFile);
        Assert.Equal(string.Empty, settings.BatFilePath);
    }

    [Fact]
    public void BatFilePath_Empty_WhenNoBatchChosenEvenWithServerPath()
    {
        var settings = new Settings { ServerPath = @"D:\DayZServer" };

        Assert.Equal(string.Empty, settings.BatFilePath);
    }

    [Fact]
    public void BatFilePath_UsesBareName_RelativeToServerPath()
    {
        var settings = new Settings { ServerPath = @"D:\DayZServer", BatchFile = "run.bat" };

        Assert.Equal(Path.Combine(@"D:\DayZServer", "run.bat"), settings.BatFilePath);
    }
	
	[Fact]
    public void BatFilePath_UsesRootedPath_AsIs()
    {
        string rooted = Path.GetFullPath(Path.Combine("subdir", "run.bat"));
        var settings = new Settings { ServerPath = @"D:\DayZServer", BatchFile = rooted };

        Assert.Equal(rooted, settings.BatFilePath);
    }
}
