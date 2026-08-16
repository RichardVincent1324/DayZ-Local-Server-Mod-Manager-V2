using DayZModManager.Core;

namespace DayZModManager.Core.Tests.Services;

public class AppPathsTests
{
    [Fact]
    public void Resolve_PrefersOverride()
    {
        Assert.Equal(@"D:\custom", AppPaths.Resolve(@"D:\custom", @"D:\server"));
    }

    [Fact]
    public void Resolve_UsesServerPathDefault_WhenNoOverride()
    {
        Assert.Equal(
            Path.Combine(@"D:\server", AppPaths.DataDirectoryName),
            AppPaths.Resolve(null, @"D:\server"));
    }

    [Fact]
    public void Resolve_FallsBackToLegacy_WhenNothingSet()
    {
        Assert.Equal(AppPaths.LegacyDirectory(), AppPaths.Resolve(null, null));
    }

    [Fact]
    public void Resolve_IgnoresBlankOverride_AndUsesServerPath()
    {
        Assert.Equal(
            Path.Combine(@"D:\server", AppPaths.DataDirectoryName),
            AppPaths.Resolve("   ", @"D:\server"));
    }

    [Fact]
    public void LegacyDirectory_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.LegacyDirectory()));
    }
}
