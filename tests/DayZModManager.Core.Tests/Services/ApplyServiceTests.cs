using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class ApplyServiceTests
{
    private const string WorkshopPath = @"D:\DayZ\!Workshop";
    private const string ServerPath = @"D:\DayZServer";
    private const string DataDirectory = @"D:\app\data";

    private static ApplyContext CreateContext(IReadOnlyList<string> loadedMods) =>
        new()
        {
            Settings = new Settings
            {
                WorkshopPath = WorkshopPath,
                ServerPath = ServerPath,
                BatFileName = "LocalServer.example.bat",
            },
            LoadedMods = loadedMods,
            DataDirectory = DataDirectory,
        };

    private static FakeFileSystem SeedValidEnvironment()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(WorkshopPath, "@CF", "@Expansion");
        fs.AddDirectory(ServerPath);
        fs.AddFile($@"{ServerPath}\DayZServer_x64.exe", "");
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", "");
        fs.AddFile($@"{ServerPath}\LocalServer.example.bat", "set \"modList=-mod=@Old;\"");
        return fs;
    }

    private static (ApplyService Service, FakeFileSystem Fs, FakeJunctionOperations Junctions) CreateRealServices(FakeFileSystem fs)
    {
        var junctions = new FakeJunctionOperations();
        var batchFile = new BatchFileService(fs);
        var service = new ApplyService(
            new SettingsService(fs),
            new ModOrderStore(fs),
            batchFile,
            new JunctionService(fs, junctions),
            new ValidationService(fs, batchFile));
        return (service, fs, junctions);
    }

    [Fact]
    public void Apply_Succeeds_AndSynchronizesEverything()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        (ApplyService service, FakeFileSystem _, FakeJunctionOperations junctions) = CreateRealServices(fs);

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.True(result.Success);

        // Configuration saved
        IReadOnlyList<string> savedOrder = new ModOrderStore(fs).Load(DataDirectory).Value!;
        Assert.Equal(new[] { "@CF" }, savedOrder);

        // Batch file updated
        Assert.Contains("modList=-mod=@CF;", fs.TryGetFileContents($@"{ServerPath}\LocalServer.example.bat")!);

        // Junction created
        Assert.True(junctions.IsJunction($@"{ServerPath}\@CF"));
    }

    [Fact]
    public void Apply_FailsValidation_AndDoesNotTouchServer()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath);
        // Missing executable and config file -> validation fails
        fs.AddFile($@"{ServerPath}\LocalServer.example.bat", "set \"modList=-mod=@Old;\"");

        (ApplyService service, FakeFileSystem _, FakeJunctionOperations junctions) = CreateRealServices(fs);

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.False(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("Validation failed"));
        Assert.Empty(junctions.Targets);
        Assert.Equal(ConfigLoadStatus.Missing, new ModOrderStore(fs).Load(DataDirectory).Status);
    }

    [Fact]
    public void Apply_AbortsBeforeJunctions_WhenBatchWriteFails()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        var junctions = new FakeJunctionOperations();
        var realBatch = new BatchFileService(fs);
        var stubBatch = new StubBatchFileService(realBatch) { WriteResult = false };

        var service = new ApplyService(
            new SettingsService(fs),
            new ModOrderStore(fs),
            stubBatch,
            new JunctionService(fs, junctions),
            new ValidationService(fs, realBatch));

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.False(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("batch file", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(junctions.Targets);
    }

    private sealed class StubBatchFileService : IBatchFileService
    {
        private readonly IBatchFileService _inner;

        public StubBatchFileService(IBatchFileService inner) => _inner = inner;

        public bool WriteResult { get; set; } = true;

        public IReadOnlyList<string> ReadModList(string batFilePath) => _inner.ReadModList(batFilePath);

        public bool HasModListLine(string batFilePath) => _inner.HasModListLine(batFilePath);

        public bool WriteModList(string batFilePath, IReadOnlyList<string> modNames) => WriteResult;

        public bool WriteServerProfile(string batFilePath, string relativeProfile) => _inner.WriteServerProfile(batFilePath, relativeProfile);
    }
}
