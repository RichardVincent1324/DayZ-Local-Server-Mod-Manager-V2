using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class ValidationServiceTests
{
    private const string WorkshopPath = @"D:\DayZ\!Workshop";
    private const string ServerPath = @"D:\DayZServer";

    private static ValidationService CreateService(FakeFileSystem fs) =>
        new(fs, new BatchFileService(fs));

    private static ValidationContext CreateContext(FakeFileSystem fs, IReadOnlyList<string> loadedMods)
    {
        var settings = new Settings
        {
            WorkshopPath = WorkshopPath,
            ServerPath = ServerPath,
            BatFileName = "LocalServer.example.bat",
        };
        return new ValidationContext { Settings = settings, LoadedMods = loadedMods };
    }

    private static FakeFileSystem SeedValidEnvironment()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(WorkshopPath, "@CF", "@Expansion");
        fs.AddDirectory(ServerPath);
        fs.AddFile($@"{ServerPath}\DayZServer_x64.exe", "");
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", "");
        fs.AddFile($@"{ServerPath}\LocalServer.example.bat", "set \"modList=-mod=@CF;\"");
        return fs;
    }

    [Fact]
    public void Validate_ReturnsNoErrors_ForValidEnvironment()
    {
        FakeFileSystem fs = SeedValidEnvironment();

        IReadOnlyList<string> errors = CreateService(fs).Validate(CreateContext(fs, new[] { "@CF" }));

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ReportsMissingWorkshop()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        fs.AddDirectory(@"D:\DayZServer");

        var context = CreateContext(fs, new[] { "@CF" });
        context = context with { Settings = context.Settings with { WorkshopPath = @"D:\missing" } };

        IReadOnlyList<string> errors = CreateService(fs).Validate(context);

        Assert.Contains(errors, e => e.Contains("Workshop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ReportsMissingExecutable()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        // Remove the executable by rebuilding without it.
        var bare = new FakeFileSystem();
        bare.AddDirectory(WorkshopPath, "@CF");
        bare.AddDirectory(ServerPath);
        bare.AddFile($@"{ServerPath}\serverDZ.cfg", "");
        bare.AddFile($@"{ServerPath}\LocalServer.example.bat", "set \"modList=-mod=@CF;\"");

        IReadOnlyList<string> errors = CreateService(bare).Validate(CreateContext(bare, new[] { "@CF" }));

        Assert.Contains(errors, e => e.Contains("DayZServer_x64.exe"));
    }

    [Fact]
    public void Validate_ReportsMissingModListLine()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        fs.AddFile($@"{ServerPath}\LocalServer.example.bat", "set \"serverName=X\"");

        IReadOnlyList<string> errors = CreateService(fs).Validate(CreateContext(fs, new[] { "@CF" }));

        Assert.Contains(errors, e => e.Contains("modList", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ReportsModNotInWorkshop()
    {
        FakeFileSystem fs = SeedValidEnvironment();

        IReadOnlyList<string> errors = CreateService(fs).Validate(CreateContext(fs, new[] { "@CF", "@Ghost" }));

        Assert.Contains(errors, e => e.Contains("@Ghost"));
    }
}
