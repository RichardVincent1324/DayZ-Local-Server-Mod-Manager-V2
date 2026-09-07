using DayZModManager.Core;

namespace DayZModManager.Core.Tests.Services;

public class AppPathsTests
{
    [Fact]
    public void Resolve_UsesServerPathDefault_WhenServerPathSet()
    {
        Assert.Equal(
            Path.Combine(@"D:\server", AppPaths.DataDirectoryName),
            AppPaths.Resolve(@"D:\server"));
    }

    [Fact]
    public void Resolve_FallsBackToLegacy_WhenNothingSet()
    {
        Assert.Equal(AppPaths.LegacyDirectory(), AppPaths.Resolve(null));
    }

    [Fact]
    public void Resolve_IgnoresBlankServerPath_AndUsesLegacy()
    {
        Assert.Equal(AppPaths.LegacyDirectory(), AppPaths.Resolve("   "));
    }

    [Fact]
    public void LegacyDirectory_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.LegacyDirectory()));
    }

    [Fact]
    public void LegacyDirectory_EndsWithDataDirectoryName()
    {
        Assert.EndsWith(AppPaths.DataDirectoryName, AppPaths.LegacyDirectory());
    }
}
