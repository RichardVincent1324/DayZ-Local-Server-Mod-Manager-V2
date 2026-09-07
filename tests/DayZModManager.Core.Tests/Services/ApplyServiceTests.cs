using System.IO;
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
        Assert.Contains("modList=-mod=ModList/@CF;", fs.TryGetFileContents($@"{ServerPath}\LocalServer.example.bat")!);

        // Junction created under the ModList folder
        Assert.True(junctions.IsJunction($@"{ServerPath}\{ModListFolder.Name}\@CF"));
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
    public void Apply_DoesNotPersistConfiguration_WhenBatchWriteFails()
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

        // Junctions are synchronized before the batch file, so they exist even
        // though the batch write failed (they are reconciled again on the next
        // Apply and are harmless while the server still points at the old list).
        Assert.True(junctions.IsJunction($@"{ServerPath}\{ModListFolder.Name}\@CF"));

        // Nothing should be persisted when the batch-file write fails.
        Assert.Equal(ConfigLoadStatus.Missing, new ModOrderStore(fs).Load(DataDirectory).Status);
        Assert.Equal(ConfigLoadStatus.Missing, new SettingsService(fs).Load(DataDirectory).Status);
    }

    [Fact]
    public void Apply_JunctionFailure_DoesNotPersistConfiguration()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        (ApplyService service, FakeFileSystem _, FakeJunctionOperations junctions) = CreateRealServices(fs);
        junctions.FailCreate = true;

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.False(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("Failed to create junction"));

        // A failed junction sync aborts before the batch file or any configuration
        // is touched, so the server still points at the previous mod list.
        Assert.Contains("modList=-mod=@Old;", fs.TryGetFileContents($@"{ServerPath}\LocalServer.example.bat")!);

        // A failed junction sync must not leave config on disk, otherwise the
        // next start would reload changes the user was told were not applied.
        Assert.Equal(ConfigLoadStatus.Missing, new ModOrderStore(fs).Load(DataDirectory).Status);
        Assert.Equal(ConfigLoadStatus.Missing, new SettingsService(fs).Load(DataDirectory).Status);
    }

    [Fact]
    public void Apply_BatchWriteThrows_KeepsJunctionsAndConfigOfUnchangedSet()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        fs.AddFile($@"{ServerPath}\LocalServer.example.bat", "set \"modList=-mod=ModList/@CF;ModList/@Expansion;\"");
        var junctions = new FakeJunctionOperations();
        string modFolder = $@"{ServerPath}\{ModListFolder.Name}";
        junctions.Create($@"{modFolder}\@CF", $@"{WorkshopPath}\@CF");
        junctions.Create($@"{modFolder}\@Expansion", $@"{WorkshopPath}\@Expansion");

        var stubBatch = new StubBatchFileService(new BatchFileService(fs)) { ThrowWrite = true };
        var service = new ApplyService(
            new SettingsService(fs),
            new ModOrderStore(fs),
            stubBatch,
            new JunctionService(fs, junctions),
            new ValidationService(fs, new BatchFileService(fs)));

        // Removing @Expansion from the loaded set, but the batch write throws.
        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.False(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("Failed to update the batch file"));

        // The launch batch file was not changed and, crucially, the junction of the
        // removed mod is still present: a failed Apply must not tear down links the
        // unchanged batch file still references.
        Assert.True(junctions.IsJunction($@"{modFolder}\@Expansion"));
        Assert.True(junctions.IsJunction($@"{modFolder}\@CF"));

        // Nothing is persisted when the batch-file write throws.
        Assert.Equal(ConfigLoadStatus.Missing, new ModOrderStore(fs).Load(DataDirectory).Status);
        Assert.Equal(ConfigLoadStatus.Missing, new SettingsService(fs).Load(DataDirectory).Status);
    }

    private sealed class StubBatchFileService : IBatchFileService
    {
        private readonly IBatchFileService _inner;

        public StubBatchFileService(IBatchFileService inner) => _inner = inner;

        public bool WriteResult { get; set; } = true;

        public bool ThrowWrite { get; set; }

        public IReadOnlyList<string> ReadModList(string batFilePath) => _inner.ReadModList(batFilePath);

        public bool HasModListLine(string batFilePath) => _inner.HasModListLine(batFilePath);

        public bool WriteModList(string batFilePath, IReadOnlyList<string> modNames)
        {
            if (ThrowWrite)
            {
                throw new IOException("batch file is locked");
            }

            return WriteResult;
        }

        public bool WriteServerProfile(string batFilePath, string relativeProfile) => _inner.WriteServerProfile(batFilePath, relativeProfile);
    }
}
