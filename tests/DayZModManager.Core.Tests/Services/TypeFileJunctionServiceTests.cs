using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class TypeFileJunctionServiceTests
{
    private const string ServerPath = @"D:\DayZServer";
    private const string MapName = "dayzOffline.chernarusplus";
    private const string DataDirectory = @"D:\app\data";

    private static string MissionPath => MissionTypeFiles.MissionPath(ServerPath, MapName);

    private static string Link => MissionTypeFiles.JunctionPath(MissionPath);

    private static string TargetA =>
        PresetPaths.TypeFilesFolder(DataDirectory, MapName, "PresetA");

    private static string TargetB =>
        PresetPaths.TypeFilesFolder(DataDirectory, MapName, "PresetB");

    private static (TypeFileJunctionService Service, FakeFileSystem Fs, FakeJunctionOperations Junctions) CreateService()
    {
        var fs = new FakeFileSystem();
        var junctions = new FakeJunctionOperations();
        fs.AddDirectory(MissionPath);
        return (new TypeFileJunctionService(fs, junctions), fs, junctions);
    }

    [Fact]
    public void Sync_CreatesJunctionAtStableMissionPath()
    {
        (TypeFileJunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(TargetA);

        JunctionSyncResult result = service.Sync(MissionPath, TargetA);

        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.Created);
        Assert.True(junctions.IsJunction(Link));
        Assert.Equal(TargetA, junctions.Targets[Link]);
        Assert.StartsWith(
            Path.Combine(ServerPath, "mpmissions", MapName, "db"),
            Link,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sync_CreatesMissingTargetFolder()
    {
        (TypeFileJunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();

        JunctionSyncResult result = service.Sync(MissionPath, TargetA);

        Assert.Equal(0, result.Failed);
        Assert.True(fs.DirectoryExists(TargetA));
        Assert.Equal(TargetA, junctions.Targets[Link]);
    }

    [Fact]
    public void Sync_ReusesCorrectJunction()
    {
        (TypeFileJunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(TargetA);
        junctions.Create(Link, TargetA);

        JunctionSyncResult result = service.Sync(MissionPath, TargetA);

        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Removed);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(TargetA, junctions.Targets[Link]);
    }

    [Fact]
    public void Prepare_DoesNotRetargetStaleJunction()
    {
        (TypeFileJunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(TargetA);
        fs.AddDirectory(TargetB);
        junctions.Create(Link, TargetA);

        JunctionSyncResult result = service.Prepare(MissionPath, TargetB);

        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(TargetA, junctions.Targets[Link]);
        Assert.Contains(result.Messages, message => message.Contains("re-pointing scheduled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Finalize_RetargetsJunctionAtSamePath()
    {
        (TypeFileJunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(TargetA);
        fs.AddDirectory(TargetB);
        junctions.Create(Link, TargetA);

        JunctionSyncResult result = service.Finalize(MissionPath, TargetB);

        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.Removed);
        Assert.Equal(1, result.Created);
        Assert.True(junctions.IsJunction(Link));
        Assert.Equal(TargetB, junctions.Targets[Link]);
    }

    [Fact]
    public void Sync_ReportsPhysicalFolderConflict()
    {
        (TypeFileJunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(TargetA);
        fs.AddDirectory(Link);

        JunctionSyncResult result = service.Sync(MissionPath, TargetA);

        // Sync runs both phases; each one reports the same conflict.
        Assert.Equal(2, result.Failed);
        Assert.Contains(result.Messages, message => message.Contains("Physical folder conflict", StringComparison.Ordinal));
        Assert.False(junctions.IsJunction(Link));
    }

    [Fact]
    public void Sync_ReportsCreateFailure()
    {
        (TypeFileJunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(TargetA);
        junctions.FailCreate = true;

        JunctionSyncResult result = service.Sync(MissionPath, TargetA);

        // Sync runs both phases; each one reports the failed create.
        Assert.Equal(2, result.Failed);
        Assert.Contains(result.Messages, message => message.Contains("Failed to create", StringComparison.Ordinal));
    }

    [Fact]
    public void Finalize_ReportsDeleteFailure_AndKeepsOldLink()
    {
        (TypeFileJunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(TargetA);
        fs.AddDirectory(TargetB);
        junctions.Create(Link, TargetA);
        junctions.FailDelete = true;

        JunctionSyncResult result = service.Finalize(MissionPath, TargetB);

        Assert.Equal(1, result.Failed);
        Assert.Contains(result.Messages, message => message.Contains("Failed to remove", StringComparison.Ordinal));
        Assert.Equal(TargetA, junctions.Targets[Link]);
    }

    [Fact]
    public void Verify_ReturnsTrueOnlyForMatchingExistingTarget()
    {
        (TypeFileJunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(TargetA);
        fs.AddDirectory(TargetB);

        Assert.False(service.Verify(MissionPath, TargetA));

        junctions.Create(Link, TargetA);
        Assert.True(service.Verify(MissionPath, TargetA));
        Assert.False(service.Verify(MissionPath, TargetB));
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenTargetFolderIsMissing()
    {
        (TypeFileJunctionService service, FakeFileSystem _, FakeJunctionOperations junctions) = CreateService();
        junctions.Create(Link, TargetA);

        Assert.False(service.Verify(MissionPath, TargetA));
    }

    [Fact]
    public void Prepare_ReportsFailure_WhenPathsAreEmpty()
    {
        (TypeFileJunctionService service, FakeFileSystem _, _) = CreateService();

        JunctionSyncResult result = service.Prepare(string.Empty, TargetA);

        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public void Prepare_ReportsFailure_WhenMissionFolderIsMissing()
    {
        var fs = new FakeFileSystem();
        var junctions = new FakeJunctionOperations();
        var service = new TypeFileJunctionService(fs, junctions);
        fs.AddDirectory(TargetA);

        JunctionSyncResult result = service.Prepare(MissionPath, TargetA);

        Assert.Equal(1, result.Failed);
        Assert.Contains(result.Messages, message => message.Contains("Mission folder not found", StringComparison.Ordinal));
        Assert.False(fs.DirectoryExists(Path.Combine(MissionPath, "db")));
        Assert.False(junctions.IsJunction(Link));
    }
}
