using DayZModManager.App.ViewModels;

namespace DayZModManager.App.Tests.ViewModels;

public class LogViewModelTests
{
    [Fact]
    public void Add_CapsEntries_ToMaxCount()
    {
        var log = new LogViewModel();

        for (int i = 0; i < 600; i++)
        {
            log.Info(i.ToString());
        }

        Assert.Equal(500, log.Entries.Count);
        Assert.EndsWith("599", log.Entries[^1].Message);
    }
}
