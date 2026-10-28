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
    private const string MapName = "dayzOffline.chernarusplus";
    private const string PresetDir = @"D:\app\data\Presets\dayzOffline.chernarusplus\__default_preset__";

    private static ApplyContext CreateContext(
        IReadOnlyList<string> loadedMods, string presetName = PresetPaths.DefaultPresetName) =>
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
            Environment = new ServerEnvironment
            {
                MapName = MapName,
                PresetName = presetName,
                PresetFolder = PresetPaths.PresetFolder(DataDirectory, MapName, presetName),
                ServerProfile = @"DayZ-Mod-Manager-V2\Presets\dayzOffline.chernarusplus\__default_preset__\profiles",
                ServerConfig = @"DayZ-Mod-Manager-V2\Presets\dayzOffline.chernarusplus\__default_preset__\serverDZ.cfg",
                JunctionKey = "1",
            },
        };

    private static FakeFileSystem SeedValidEnvironment()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(WorkshopPath, "@CF", "@Expansion");
        fs.AddDirectory(ServerPath);
        fs.AddDirectory(MissionTypeFiles.MissionPath(ServerPath, MapName));
        fs.AddFile($@"{ServerPath}\DayZServer_x64.exe", "");
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", "");
        fs.AddFile($@"{ServerPath}\LocalServer.example.bat", "set \"modList=-mod=@Old;\"\nset \"serverProfile=old\\profiles\"\nset \"serverConfig=old\\serverDZ.cfg\"");
        return fs;
    }

    private static (ApplyService Service, FakeFileSystem Fs, FakeJunctionOperations Junctions) CreateRealServices(FakeFileSystem fs)
    {
        var junctions = new FakeJunctionOperations();
        var batchFile = new BatchFileService(fs);
        var service = new ApplyService(
            new SettingsService(fs),
            new ModOrderStore(fs),
            new LaunchEnvironmentService(batchFile),
            new JunctionService(fs, junctions),
            new TypeFileJunctionService(fs, junctions),
            new ValidationService(fs, batchFile),
            fs);
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
        IReadOnlyList<string> savedOrder = new ModOrderStore(fs).Load(PresetDir).Value!;
        Assert.Equal(new[] { "@CF" }, savedOrder);

        // Batch file updated
        Assert.Contains("modList=-mod=DZMMV2_ModList/1/@CF;", fs.TryGetFileContents($@"{ServerPath}\LocalServer.example.bat")!);

        // Junction created under the preset's DZMMV2_ModList subfolder
        Assert.True(junctions.IsJunction(Path.Combine(Dzmmv2ModListFolder.Directory(ServerPath, "1"), "@CF")));
    }

    [Fact]
    public void Apply_CreatesMissionLocalTypeFilesJunction()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        (ApplyService service, FakeFileSystem _, FakeJunctionOperations junctions) = CreateRealServices(fs);

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.True(result.Success);

        string link = MissionTypeFiles.JunctionPath(
            MissionTypeFiles.MissionPath(ServerPath, MapName));
        string target = PresetPaths.TypeFilesFolder(
            DataDirectory, MapName, PresetPaths.DefaultPresetName);

        Assert.True(junctions.IsJunction(link));
        Assert.Equal(target, junctions.Targets[link]);
    }

    [Fact]
    public void Apply_RepointsSameMissionTypeFilesJunction_WhenPresetChanges()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        (ApplyService service, FakeFileSystem _, FakeJunctionOperations junctions) = CreateRealServices(fs);

        string link = MissionTypeFiles.JunctionPath(
            MissionTypeFiles.MissionPath(ServerPath, MapName));

        // The link already exists for another preset on the same map.
        junctions.Create(
            link, PresetPaths.TypeFilesFolder(DataDirectory, MapName, PresetPaths.DefaultPresetName));

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }, "Alpha"));

        Assert.True(result.Success);

        // The path stayed stable while its target moved to the new preset.
        Assert.True(junctions.IsJunction(link));
        Assert.Equal(
            PresetPaths.TypeFilesFolder(DataDirectory, MapName, "Alpha"),
            junctions.Targets[link]);
    }

    [Fact]
    public void Apply_TypeFilesJunctionFinalizeFailure_DoesNotFailCommittedApply()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        (ApplyService service, FakeFileSystem _, FakeJunctionOperations junctions) = CreateRealServices(fs);

        string link = MissionTypeFiles.JunctionPath(
            MissionTypeFiles.MissionPath(ServerPath, MapName));
        string oldTarget = PresetPaths.TypeFilesFolder(
            DataDirectory, MapName, "PreviousPreset");
        junctions.Create(link, oldTarget);
        junctions.FailDelete = true;

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }, "Alpha"));

        // Finalization is destructive and runs after the configuration commit, so
        // a stuck link must not roll back an otherwise successful Apply.
        Assert.True(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("could not be re-targeted"));

        IReadOnlyList<string> savedOrder = new ModOrderStore(fs).Load(
            PresetPaths.PresetFolder(DataDirectory, MapName, "Alpha")).Value!;
        Assert.Equal(new[] { "@CF" }, savedOrder);
        Assert.Equal(oldTarget, junctions.Targets[link]);
        Assert.False(new TypeFileJunctionService(fs, junctions).Verify(
            MissionTypeFiles.MissionPath(ServerPath, MapName),
            PresetPaths.TypeFilesFolder(DataDirectory, MapName, "Alpha")));
    }

    [Fact]
    public void Apply_TypeFilesJunctionFinalizeThrows_DoesNotFailCommittedApply()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        var junctions = new FakeJunctionOperations();
        var batchFile = new BatchFileService(fs);
        var service = new ApplyService(
            new SettingsService(fs),
            new ModOrderStore(fs),
            new LaunchEnvironmentService(batchFile),
            new JunctionService(fs, junctions),
            new ThrowingFinalizeTypeFileJunctionService(),
            new ValidationService(fs, batchFile),
            fs);

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.True(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("Failed to finalize the type files junction"));

        IReadOnlyList<string> savedOrder = new ModOrderStore(fs).Load(PresetDir).Value!;
        Assert.Equal(new[] { "@CF" }, savedOrder);
    }

    [Fact]
    public void Apply_TypeFilesJunctionConflict_AbortsBeforeBatchAndConfig()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        string link = MissionTypeFiles.JunctionPath(
            MissionTypeFiles.MissionPath(ServerPath, MapName));
        fs.AddDirectory(link);

        (ApplyService service, FakeFileSystem _, _) = CreateRealServices(fs);

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.False(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("Physical folder conflict"));
        Assert.Contains("modList=-mod=@Old;", fs.TryGetFileContents($@"{ServerPath}\LocalServer.example.bat")!);
        Assert.Equal(ConfigLoadStatus.Missing, new ModOrderStore(fs).Load(PresetDir).Status);
        Assert.Equal(ConfigLoadStatus.Missing, new SettingsService(fs).Load(DataDirectory).Status);
    }

    [Fact]
    public void Apply_WithMissingMod_KeepsItInModListWithoutJunction_AndSucceeds()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        (ApplyService service, FakeFileSystem _, FakeJunctionOperations junctions) = CreateRealServices(fs);

        // @Ghost is loaded but absent from the workshop. A missing mod is tolerated:
        // it stays in the -mod line and simply gets no junction. Only a launch is
        // blocked by it.
        ApplyResult result = service.Apply(CreateContext(new[] { "@CF", "@Ghost" }));

        Assert.True(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("@Ghost"));

        string batch = fs.TryGetFileContents($@"{ServerPath}\LocalServer.example.bat")!;
        Assert.Contains("DZMMV2_ModList/1/@Ghost;", batch);

        Assert.True(junctions.IsJunction(Path.Combine(Dzmmv2ModListFolder.Directory(ServerPath, "1"), "@CF")));
        Assert.False(junctions.IsJunction(Path.Combine(Dzmmv2ModListFolder.Directory(ServerPath, "1"), "@Ghost")));

        IReadOnlyList<string> savedOrder = new ModOrderStore(fs).Load(PresetDir).Value!;
        Assert.Equal(new[] { "@CF", "@Ghost" }, savedOrder);
    }

    [Fact]
    public void Apply_FailsValidation_AndDoesNotTouchServer()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath);
        // Missing executable and config file -> validation fails
        fs.AddFile($@"{ServerPath}\LocalServer.example.bat", "set \"modList=-mod=@Old;\"\nset \"serverProfile=old\\profiles\"\nset \"serverConfig=old\\serverDZ.cfg\"");

        (ApplyService service, FakeFileSystem _, FakeJunctionOperations junctions) = CreateRealServices(fs);

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.False(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("Validation failed"));
        Assert.Empty(junctions.Targets);
        Assert.Equal(ConfigLoadStatus.Missing, new ModOrderStore(fs).Load(PresetDir).Status);
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
            new LaunchEnvironmentService(stubBatch),
            new JunctionService(fs, junctions),
            new TypeFileJunctionService(fs, junctions),
            new ValidationService(fs, realBatch),
            fs);

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.False(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("batch file", StringComparison.OrdinalIgnoreCase));

        // Junctions are synchronized before the batch file, so they exist even
        // though the batch write failed (they are reconciled again on the next
        // Apply and are harmless while the server still points at the old list).
        Assert.True(junctions.IsJunction(Path.Combine(Dzmmv2ModListFolder.Directory(ServerPath, "1"), "@CF")));

        // Nothing should be persisted when the batch-file write fails.
        Assert.Equal(ConfigLoadStatus.Missing, new ModOrderStore(fs).Load(PresetDir).Status);
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
        Assert.Equal(ConfigLoadStatus.Missing, new ModOrderStore(fs).Load(PresetDir).Status);
        Assert.Equal(ConfigLoadStatus.Missing, new SettingsService(fs).Load(DataDirectory).Status);
    }

    [Fact]
    public void Apply_BatchWriteThrows_KeepsJunctionsAndConfigOfUnchangedSet()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        fs.AddFile($@"{ServerPath}\LocalServer.example.bat", "set \"modList=-mod=DZMMV2_ModList/@CF;DZMMV2_ModList/@Expansion;\"\nset \"serverProfile=old\\profiles\"\nset \"serverConfig=old\\serverDZ.cfg\"");
        var junctions = new FakeJunctionOperations();
        string modFolder = Dzmmv2ModListFolder.Directory(ServerPath, "1");
        junctions.Create($@"{modFolder}\@CF", $@"{WorkshopPath}\@CF");
        junctions.Create($@"{modFolder}\@Expansion", $@"{WorkshopPath}\@Expansion");

        var stubBatch = new StubBatchFileService(new BatchFileService(fs)) { ThrowWrite = true };
        var service = new ApplyService(
            new SettingsService(fs),
            new ModOrderStore(fs),
            new LaunchEnvironmentService(stubBatch),
            new JunctionService(fs, junctions),
            new TypeFileJunctionService(fs, junctions),
            new ValidationService(fs, new BatchFileService(fs)),
            fs);

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
        Assert.Equal(ConfigLoadStatus.Missing, new ModOrderStore(fs).Load(PresetDir).Status);
        Assert.Equal(ConfigLoadStatus.Missing, new SettingsService(fs).Load(DataDirectory).Status);
    }

    [Fact]
    public void Apply_WhenConfigSaveFails_RestoresTheBatchFile()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        var junctions = new FakeJunctionOperations();
        var batchFile = new BatchFileService(fs);
        string batchPath = $@"{ServerPath}\LocalServer.example.bat";
        string originalBatch = fs.TryGetFileContents(batchPath)!;

        var service = new ApplyService(
            new ThrowingSettingsService(new SettingsService(fs)),
            new ModOrderStore(fs),
            new LaunchEnvironmentService(batchFile),
            new JunctionService(fs, junctions),
            new TypeFileJunctionService(fs, junctions),
            new ValidationService(fs, batchFile),
            fs);

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.False(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("Failed to save the configuration"));

        // The launcher must not reference a mod list that was never committed.
        Assert.Equal(originalBatch, fs.TryGetFileContents(batchPath));
        Assert.Equal(ConfigLoadStatus.Missing, new ModOrderStore(fs).Load(PresetDir).Status);
    }

    [Fact]
    public void Apply_ValidatesActivePresetConfig_NotServerRoot()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        // Only the active preset's config exists; the server root has none. The
        // batch file points the server at the preset config, so that is what must
        // be validated.
        fs.DeleteFile($@"{ServerPath}\serverDZ.cfg");
        fs.AddFile(
            Path.Combine(ServerPath, "DayZ-Mod-Manager-V2", "Presets", "dayzOffline.chernarusplus", "__default_preset__", "serverDZ.cfg"),
            "");

        var (service, _, _) = CreateRealServices(fs);

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.True(result.Success);
    }

    [Fact]
    public void Apply_MissingServerConfigLine_Fails_AndDoesNotOverwriteServerRootConfig()
    {
        FakeFileSystem fs = SeedValidEnvironment();
        string rootConfig = $@"{ServerPath}\serverDZ.cfg";
        fs.AddFile(rootConfig, "root config");
        fs.AddFile(Path.Combine(PresetDir, "serverDZ.cfg"), "preset config");
        fs.AddFile(
            $@"{ServerPath}\LocalServer.example.bat",
            "set \"modList=-mod=@Old;\"\nset \"serverProfile=old\\profiles\"");

        (ApplyService service, _, _) = CreateRealServices(fs);

        ApplyResult result = service.Apply(CreateContext(new[] { "@CF" }));

        Assert.False(result.Success);
        Assert.Contains(result.Logs, l => l.Contains("serverConfig", StringComparison.OrdinalIgnoreCase));

        // The old behaviour copied the preset config over the root config. That
        // fallback is gone: the root config and the batch file stay untouched.
        Assert.Equal("root config", fs.TryGetFileContents(rootConfig));
        Assert.Contains("modList=-mod=@Old;", fs.TryGetFileContents($@"{ServerPath}\LocalServer.example.bat")!);
    }

    private sealed class ThrowingFinalizeTypeFileJunctionService : ITypeFileJunctionService
    {
        public JunctionSyncResult Prepare(string missionPath, string targetFolder) => new();

        public JunctionSyncResult Finalize(string missionPath, string targetFolder) =>
            throw new IOException("junction is locked");

        public JunctionSyncResult Sync(string missionPath, string targetFolder) =>
            throw new IOException("junction is locked");

        public bool Verify(string missionPath, string targetFolder) => true;
    }

    private sealed class ThrowingSettingsService : ISettingsService
    {
        private readonly ISettingsService _inner;

        public ThrowingSettingsService(ISettingsService inner) => _inner = inner;

        public ConfigLoadResult<Settings> Load(string dataDirectory) => _inner.Load(dataDirectory);

        public bool BackupCorrupt(string dataDirectory) => _inner.BackupCorrupt(dataDirectory);

        public void Save(string dataDirectory, Settings settings) =>
            throw new IOException("data directory is read-only");
    }

    private sealed class StubBatchFileService : IBatchFileService
    {
        private readonly IBatchFileService _inner;

        public StubBatchFileService(IBatchFileService inner) => _inner = inner;

        public bool WriteResult { get; set; } = true;

        public bool ThrowWrite { get; set; }

        public bool HasModListLine(string batFilePath) => _inner.HasModListLine(batFilePath);

        public bool HasServerProfileLine(string batFilePath) => _inner.HasServerProfileLine(batFilePath);

        public bool HasServerConfigLine(string batFilePath) => _inner.HasServerConfigLine(batFilePath);

        public bool WriteModList(string batFilePath, IReadOnlyList<string> modNames)
        {
            if (ThrowWrite)
            {
                throw new IOException("batch file is locked");
            }

            return WriteResult;
        }

        public bool WriteServerProfile(string batFilePath, string relativeProfile) => _inner.WriteServerProfile(batFilePath, relativeProfile);

        public bool WriteServerConfig(string batFilePath, string serverConfigPath) => _inner.WriteServerConfig(batFilePath, serverConfigPath);
    }
}
