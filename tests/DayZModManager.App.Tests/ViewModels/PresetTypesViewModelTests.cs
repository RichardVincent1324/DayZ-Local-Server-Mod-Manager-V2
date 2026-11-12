using System.IO;
using DayZModManager.Core;
using DayZModManager.App.Services;
using DayZModManager.App.ViewModels;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;

namespace DayZModManager.App.Tests.ViewModels;

public partial class PresetTypesViewModelTests
{
    private const string ServerPath = @"D:\DayZServer";

    private const string MapName = "dayzOffline.chernarusplus";

    private static PresetTypesViewModel Create(
        TypesConfig config,
        IMapService? mapService = null,
        ITypeFilesConfigStore? typesConfigStore = null,
        FakeTypesService? typesService = null,
        FakeDialogs? dialogs = null,
        FakeSaveGameService? saveGameService = null,
        FakeServerProcessState? serverProcess = null,
        FakeFileSystem? fileSystem = null,
        FakeBatchFileService? batchFileService = null,
        FakeProcessLauncher? processLauncher = null,
        FakePresetService? presetService = null,
        IJunctionService? junctionService = null,
        FakeTypeFileJunctionService? typeFileJunctionService = null,
        LogViewModel? log = null)
    {
        mapService ??= new FakeMapService(new MapInfo(MapName, $@"{ServerPath}\mpmissions\{MapName}"));
        typesConfigStore ??= new FakeTypesConfigStore();
        typesService ??= new FakeTypesService();
        dialogs ??= new FakeDialogs();
        saveGameService ??= new FakeSaveGameService();
        serverProcess ??= new FakeServerProcessState();
        fileSystem ??= new FakeFileSystem();
        batchFileService ??= new FakeBatchFileService();
        processLauncher ??= new FakeProcessLauncher();
        presetService ??= new FakePresetService();
        junctionService ??= new FakeJunctionService();
        typeFileJunctionService ??= new FakeTypeFileJunctionService();

        var vm = new PresetTypesViewModel(
            mapService,
            typesService,
            saveGameService,
            typesConfigStore,
            new FakeServerConfigService(),
            fileSystem,
            dialogs,
            log ?? new LogViewModel(),
            new FakeDataDirectoryProvider(),
            serverProcess,
            processLauncher,
            presetService,
            junctionService,
            typeFileJunctionService,
            new LaunchEnvironmentService(batchFileService));

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PresetTypesViewModel.ActiveMap))
            {
                config.CurrentMap = vm.ActiveMap ?? string.Empty;
            }
            else if (e.PropertyName == nameof(PresetTypesViewModel.TypeFilesConfigCorrupt))
            {
                config.IsCorrupt = vm.TypeFilesConfigCorrupt;
            }
        };

        vm.ActiveMap = config.CurrentMap;
        vm.ReloadTypeFilesConfig();
        return vm;
    }

    private static string PresetFolder(string mapName) =>
        Path.Combine(@"D:\data", "Presets", mapName, "__default_preset__");

    private static string PresetTypeFilesFolder(string mapName) =>
        Path.Combine(PresetFolder(mapName), "type_files");

    private static string PresetProfilesFolder(string mapName) =>
        Path.Combine(PresetFolder(mapName), "profiles");

    private static string PresetSavesFolder(string mapName) =>
        Path.Combine(PresetFolder(mapName), "saves");

    private static string PresetServerConfig(string mapName) =>
        Path.Combine(PresetFolder(mapName), "serverDZ.cfg");

    private static Settings Settings() => new()
    {
        ServerPath = ServerPath,
        WorkshopPath = @"D:\workshop",
        BatFileName = "run.bat",
    };

    private static (PresetTypesViewModel Vm, FakeTypesService Types, FakeDialogs Dialogs) CreateTypesVm(
        TypesConfig config,
        FakeTypesService types,
        FakeDialogs dialogs,
        IReadOnlyList<string>? workshopMods = null,
        IReadOnlyList<string>? loadedMods = null,
        FakeSaveGameService? saveGameService = null,
        FakeFileSystem? fileSystem = null,
        ITypeFilesConfigStore? typesConfigStore = null,
        FakeProcessLauncher? processLauncher = null,
        LogViewModel? log = null)
    {
        workshopMods ??= new[] { "@CF" };
        loadedMods ??= new[] { "@CF" };

        PresetTypesViewModel vm = Create(
            config,
            mapService: new FakeMapService(new MapInfo(MapName, $@"{ServerPath}\mpmissions\{MapName}")),
            typesConfigStore: typesConfigStore ?? new FakeTypesConfigStore(),
            typesService: types,
            dialogs: dialogs,
            saveGameService: saveGameService,
            fileSystem: fileSystem,
            processLauncher: processLauncher,
            log: log);
        vm.Refresh(Settings(), workshopMods, loadedMods);
        return (vm, types, dialogs);
    }

    private static void SelectSave(PresetTypesViewModel vm, string name) =>
        vm.SelectedSave = vm.SaveNames.First(entry => entry.Name == name && !entry.IsOrphaned);

    private static string Entry(string modName, params string[] generatedFiles) =>
        generatedFiles.FirstOrDefault() ?? modName;

    private static TypesConfig ConfigWithEntry(string entry) =>
        new() { CurrentMap = MapName };

    private static TypesConfig AppliedConfig() =>
        new() { CurrentMap = MapName };

    /// <summary>Minimal test-only holder for the active map/corrupt state.</summary>
    private sealed class TypesConfig
    {
        public string CurrentMap { get; set; } = string.Empty;

        public bool IsCorrupt { get; set; }
    }

    private static string TypeFilesFolderPath() =>
        PresetTypeFilesFolder(MapName);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private sealed class FakeMapService : IMapService
    {
        private readonly IReadOnlyList<MapInfo> _maps;

        public FakeMapService(params MapInfo[] maps) => _maps = maps;

        public IReadOnlyList<MapInfo> DiscoverMaps(string serverPath) => _maps;

        public string? ResolveMapPath(string serverPath, string mapName) =>
            _maps.FirstOrDefault(m => m.Name == mapName)?.Path;
    }

    private sealed class FakeTypesService : ITypesService
    {
        public IReadOnlyList<string> DiscoveryFiles { get; set; } = Array.Empty<string>();

        public int ImportFilesCalls { get; private set; }

        public IReadOnlyList<string>? LastImportSourceFiles { get; private set; }

        public string? LastImportTypeFilesFolder { get; private set; }

        public bool LastImportOverwrite { get; private set; }

        public TypesOperationResult ImportResult { get; set; } = new() { Success = true };

        public int SyncEconomyCoreCalls { get; private set; }

        public string? LastEconomyFolder { get; private set; }

        public string? LastTypeFilesFolder { get; private set; }

        public IReadOnlyDictionary<string, string>? LastAssignments { get; private set; }

        public TypesOperationResult SyncEconomyCoreResult { get; set; } = new() { Success = true };

        public string? ActiveTypesFolder { get; set; }

        public IReadOnlyList<string> ActiveTypeFiles { get; set; } = Array.Empty<string>();

        public IReadOnlyList<ManagedTypeFileEntry> ActiveTypeFileEntries { get; set; } = Array.Empty<ManagedTypeFileEntry>();

        public IReadOnlyList<string> ManagedTypeFileNames { get; set; } = Array.Empty<string>();

        public IReadOnlyList<ManagedTypeFileEntry> ManagedTypeFileEntries { get; set; } = Array.Empty<ManagedTypeFileEntry>();

        public IReadOnlyList<string> DiscoverXmlFiles(string workshopPath, string modName) => DiscoveryFiles;

        public TypesOperationResult ImportFiles(
            IReadOnlyList<string> sourceFiles, string typeFilesFolder, bool overwrite)
        {
            ImportFilesCalls++;
            LastImportSourceFiles = sourceFiles.ToList();
            LastImportTypeFilesFolder = typeFilesFolder;
            LastImportOverwrite = overwrite;
            return ImportResult;
        }

        public IReadOnlyList<ManagedTypeFileEntry> GetActiveTypeFiles(
            string typeFilesFolder, IReadOnlyDictionary<string, string> assignments) =>
            ActiveTypeFileEntries.Count > 0
                ? ActiveTypeFileEntries
                : ActiveTypeFiles.Select(name => new ManagedTypeFileEntry(name, TypesFileRoles.Types)).ToList();

        public TypesOperationResult SyncEconomyCore(
            string missionPath, string folder, string typeFilesFolder,
            IReadOnlyDictionary<string, string> assignments)
        {
            SyncEconomyCoreCalls++;
            LastEconomyFolder = folder;
            LastTypeFilesFolder = typeFilesFolder;
            LastAssignments = new Dictionary<string, string>(assignments, StringComparer.OrdinalIgnoreCase);
            return SyncEconomyCoreResult;
        }

        public string? GetActiveTypesFolder(string missionPath) => ActiveTypesFolder;

        public IReadOnlyList<ManagedTypeFileEntry> GetManagedTypeFiles(string missionPath) =>
            ManagedTypeFileEntries.Count > 0
                ? ManagedTypeFileEntries
                : ManagedTypeFileNames.Select(name => new ManagedTypeFileEntry(name, TypesFileRoles.Types)).ToList();
    }

    private sealed class FakeTypesConfigStore : ITypeFilesConfigStore
    {
        public bool SaveCalled { get; private set; }

        public int LoadCalls { get; private set; }

        public bool BackupCalled { get; private set; }

        public ConfigLoadResult<Dictionary<string, string>> LoadResult { get; set; } =
            ConfigLoadResult<Dictionary<string, string>>.Missing();

        public Dictionary<string, string> LastSaved { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public Exception? SaveException { get; set; }

        public ConfigLoadResult<Dictionary<string, string>> Load(string presetFolder)
        {
            LoadCalls++;
            return LoadResult;
        }

        public void Save(string presetFolder, IReadOnlyDictionary<string, string> assignments)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SaveCalled = true;
            LastSaved = new Dictionary<string, string>(assignments, StringComparer.OrdinalIgnoreCase);
        }

        public bool BackupCorrupt(string presetFolder)
        {
            BackupCalled = true;
            return true;
        }
    }

    private sealed class FakeServerConfigService : IServerConfigService
    {
        public bool UpdateTemplate(string serverPath, string missionFolderName) => true;

        public bool WriteInstanceId(string serverConfigPath, int instanceId) => true;

        public int? TryReadInstanceId(string serverConfigPath) => null;
    }

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        public string? LastOpenedFolder { get; private set; }

        public void Launch(string filePath, string workingDirectory) { }

        public void OpenFolder(string path) => LastOpenedFolder = path;
    }

    private sealed class FakeBatchFileService : IBatchFileService
    {
        public bool HasModListLine(string batFilePath) => true;

        public bool HasServerProfileLine(string batFilePath) => true;

        public bool HasServerConfigLine(string batFilePath) => true;

        public bool WriteModList(string batFilePath, IReadOnlyList<string> modNames) => true;

        public string? LastServerProfile { get; private set; }

        public bool WriteServerProfile(string batFilePath, string relativeProfile)
        {
            LastServerProfile = relativeProfile;
            return true;
        }

        public string? LastServerConfig { get; private set; }

        public bool WriteServerConfig(string batFilePath, string serverConfigPath)
        {
            LastServerConfig = serverConfigPath;
            return true;
        }
    }

    private sealed class FakeSaveGameService : ISaveGameService
    {
        public IReadOnlyList<string> StoredSaves { get; set; } = Array.Empty<string>();

        public int AddSaveCalls { get; private set; }

        public string? LastAddName { get; private set; }

        public bool LastAddOverwrite { get; private set; }

        public string? LastAddSavesFolder { get; private set; }

        public int LoadSaveCalls { get; private set; }

        public string? LastLoadName { get; private set; }

        public string? LastLoadSavesFolder { get; private set; }

        public int WipeWorldCalls { get; private set; }

        /// <summary>When set, WipeWorld blocks on this gate so tests can observe the in-flight state.</summary>
        public TaskCompletionSource? WipeWorldGate { get; set; }

        public int DeleteSaveCalls { get; private set; }

        public string? LastDeleteName { get; private set; }

        public string? LastDeleteSavesFolder { get; private set; }

        public int LastInstanceId { get; private set; }

        public string GetStorageFolderPath(string serverPath, string mapName, int instanceId) =>
            Path.Combine(serverPath, "mpmissions", mapName, $"storage_{instanceId}");

        public IReadOnlyList<int> StorageInstanceIds { get; set; } = Array.Empty<int>();

        public int DeleteStorageCalls { get; private set; }

        public int LastDeleteStorageInstanceId { get; private set; }

        public IReadOnlyList<int> ListStorageInstanceIds(string serverPath, string mapName) => StorageInstanceIds;

        public SaveGameResult DeleteStorage(string serverPath, string mapName, int instanceId)
        {
            DeleteStorageCalls++;
            LastDeleteStorageInstanceId = instanceId;
            return new SaveGameResult { Success = true, Message = $"Deleted storage_{instanceId}." };
        }

        public IReadOnlyList<string> ListSaves(string savesFolder) => StoredSaves;

        public string GetSaveFolderPath(string savesFolder, string saveName) =>
            Path.Combine(savesFolder, saveName);

        public SaveGameResult AddSave(
            string serverPath, string mapName, string savesFolder, int instanceId, string saveName, bool overwrite)
        {
            AddSaveCalls++;
            LastAddName = saveName;
            LastAddOverwrite = overwrite;
            LastAddSavesFolder = savesFolder;
            LastInstanceId = instanceId;
            return new SaveGameResult { Success = true, Message = $"Saved \"{saveName}\"." };
        }

        public SaveGameResult LoadSave(
            string serverPath, string mapName, string savesFolder, int instanceId, string saveName)
        {
            LoadSaveCalls++;
            LastLoadName = saveName;
            LastLoadSavesFolder = savesFolder;
            LastInstanceId = instanceId;
            return new SaveGameResult { Success = true, Message = $"Loaded \"{saveName}\"." };
        }

        public ConfigLoadResult<SaveMetaData> GetMeta(string savesFolder, string saveName) =>
            ConfigLoadResult<SaveMetaData>.Missing();

        public SaveGameResult WipeWorld(string serverPath, string mapName, int instanceId)
        {
            WipeWorldCalls++;
            LastInstanceId = instanceId;
            WipeWorldGate?.Task.GetAwaiter().GetResult();
            return new SaveGameResult { Success = true, Message = "World wiped." };
        }

        public SaveGameResult DeleteSave(string savesFolder, string saveName)
        {
            DeleteSaveCalls++;
            LastDeleteName = saveName;
            LastDeleteSavesFolder = savesFolder;
            return new SaveGameResult { Success = true, Message = $"Deleted \"{saveName}\"." };
        }

        public int RenameSaveCalls { get; private set; }

        public string? LastRenameSavesFolder { get; private set; }

        public string? LastRenameOldName { get; private set; }

        public string? LastRenameNewName { get; private set; }

        public SaveGameResult RenameSave(string savesFolder, string saveName, string newName)
        {
            RenameSaveCalls++;
            LastRenameSavesFolder = savesFolder;
            LastRenameOldName = saveName;
            LastRenameNewName = newName;
            return new SaveGameResult { Success = true, Message = $"Renamed \"{saveName}\" to \"{newName}\"." };
        }
    }

    private sealed class FakePresetService : IPresetService
    {
        public List<string> PresetNames { get; } = new() { PresetPaths.DefaultPresetName };

        public List<string> Created { get; } = new();

        public List<(string SourceMap, string Source, string TargetMap, string NewName)> Duplicated { get; } = new();

        public List<(string OldName, string NewName)> Renamed { get; } = new();

        public List<string> Deleted { get; } = new();

        public bool IsDefaultPreset(string? presetName) => PresetPaths.IsDefaultPreset(presetName);

        public string GetPresetFolder(string dataDirectory, string mapName, string presetName) =>
            PresetPaths.PresetFolder(dataDirectory, mapName, presetName);

        public bool PresetExists(string dataDirectory, string mapName, string presetName) =>
            PresetNames.Any(n => string.Equals(n, presetName, StringComparison.Ordinal));

        public string? DescribeIncompletePreset(string dataDirectory, string mapName, string presetName) =>
            PresetExists(dataDirectory, mapName, presetName)
                ? null
                : $"serverDZ.cfg is missing for {presetName}";

        public EnvironmentResolution ResolveEnvironment(
            Settings settings, string dataDirectory, string mapName, string presetName,
            string? presetLookupDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(mapName))
            {
                return new EnvironmentResolution(null, null);
            }

            string lookup = string.IsNullOrWhiteSpace(presetLookupDirectory) ? dataDirectory : presetLookupDirectory;
            string active = string.IsNullOrWhiteSpace(presetName) ? PresetPaths.DefaultPresetName : presetName;
            if (!PresetExists(lookup, mapName, active))
            {
                active = PresetPaths.DefaultPresetName;
            }

            if (!PresetExists(lookup, mapName, active))
            {
                return EnvironmentResolution.Failed($"preset \"{active}\" is incomplete");
            }

            return EnvironmentResolution.Resolved(new ServerEnvironment
            {
                MapName = mapName,
                PresetName = active,
                PresetFolder = PresetPaths.PresetFolder(dataDirectory, mapName, active),
                ServerProfile = PresetPaths.RelativeToServer(
                    settings.ServerPath, PresetPaths.ProfilesFolder(dataDirectory, mapName, active)),
                ServerConfig = PresetPaths.RelativeToServer(
                    settings.ServerPath, PresetPaths.ServerConfigPath(dataDirectory, mapName, active)),
                JunctionKey = Dzmmv2ModListFolder.PresetKey(ReadInstanceId(lookup, mapName, active)),
            });
        }

        public IReadOnlyList<string> ListPresetNames(string dataDirectory, string mapName) =>
            PresetNames.ToList();

        public int ReadInstanceId(string dataDirectory, string mapName, string presetName) => 1;

        public IReadOnlyList<string> FindInstanceIdIssues(string dataDirectory) => Array.Empty<string>();

        public IReadOnlyList<string> FindInstanceIdConflicts(string dataDirectory) => Array.Empty<string>();

        public int AllocateInstanceId(string dataDirectory, string serverPath, string mapName) => 1;

        public PresetResult EnsureDefaultPreset(string serverPath, string dataDirectory, string mapName)
        {
            if (!PresetNames.Any(PresetPaths.IsDefaultPreset))
            {
                PresetNames.Insert(0, PresetPaths.DefaultPresetName);
            }

            return new() { Success = true };
        }

        public PresetResult CreatePreset(
            string serverPath, string dataDirectory, string mapName, string presetName)
        {
            if (PresetNames.Any(n => string.Equals(n, presetName, StringComparison.OrdinalIgnoreCase)))
            {
                return new() { Success = false, Message = $"A preset named \"{presetName}\" already exists." };
            }

            PresetNames.Add(presetName);
            Created.Add(presetName);
            return new() { Success = true, Message = $"Created \"{presetName}\"." };
        }

        public PresetResult DuplicatePreset(
            string serverPath, string dataDirectory,
            string sourceMapName, string sourcePresetName,
            string targetMapName, string newPresetName)
        {
            if (PresetNames.Any(n => string.Equals(n, newPresetName, StringComparison.OrdinalIgnoreCase)))
            {
                return new() { Success = false, Message = $"A preset named \"{newPresetName}\" already exists." };
            }

            PresetNames.Add(newPresetName);
            Duplicated.Add((sourceMapName, sourcePresetName, targetMapName, newPresetName));
            return new() { Success = true, Message = $"Duplicated \"{sourcePresetName}\" to \"{newPresetName}\"." };
        }

        public PresetResult RenamePreset(string dataDirectory, string mapName, string presetName, string newName)
        {
            int index = PresetNames.FindIndex(n => string.Equals(n, presetName, StringComparison.Ordinal));
            if (index < 0)
            {
                return new() { Success = false, Message = "missing" };
            }

            PresetNames[index] = newName;
            Renamed.Add((presetName, newName));
            return new() { Success = true };
        }

        public PresetResult DeletePreset(string dataDirectory, string mapName, string presetName)
        {
            PresetNames.RemoveAll(n => string.Equals(n, presetName, StringComparison.Ordinal));
            Deleted.Add(presetName);
            return new() { Success = true };
        }
    }

    private sealed class FakeJunctionService : IJunctionService
    {
        public List<(string ServerPath, string PresetKey)> DeletedFolders { get; } = new();

        public JunctionSyncResult Sync(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods) =>
            new();

        public JunctionSyncResult PrepareLoaded(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods) =>
            new();

        public JunctionSyncResult Finalize(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods) =>
            new();

        public IReadOnlyList<string> FindOrphanedJunctions(string serverPath, string presetKey, IReadOnlySet<string> validModNames) =>
            Array.Empty<string>();

        public IReadOnlyList<string> Verify(string serverPath, string presetKey, IReadOnlyList<string> loadedMods) =>
            Array.Empty<string>();

        public void DeleteJunctionFolder(string serverPath, string presetKey) =>
            DeletedFolders.Add((serverPath, presetKey));
    }

    private sealed class FakeTypeFileJunctionService : ITypeFileJunctionService
    {
        public bool VerifyResult { get; set; } = true;

        public int VerifyCalls { get; private set; }

        public string? LastMissionPath { get; private set; }

        public string? LastTargetFolder { get; private set; }

        public JunctionSyncResult Prepare(string missionPath, string targetFolder) => new();

        public JunctionSyncResult Finalize(string missionPath, string targetFolder) => new();

        public JunctionSyncResult Sync(string missionPath, string targetFolder) => new();

        public bool Verify(string missionPath, string targetFolder)
        {
            VerifyCalls++;
            LastMissionPath = missionPath;
            LastTargetFolder = targetFolder;
            return VerifyResult;
        }
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path, string contents = "") => _files.Add(path);

        public void AddDirectory(string path) => _directories.Add(path);

        public bool ThrowOnStorageDirectoryExists { get; set; }

        public bool DirectoryExists(string path) =>
            ThrowOnStorageDirectoryExists && path.Contains("storage_", StringComparison.OrdinalIgnoreCase)
                ? throw new IOException("Simulated storage folder check failure.")
                : _directories.Contains(path);

        public IReadOnlyList<string> GetDirectories(string path) => Array.Empty<string>();

        public bool FileExists(string path) => _files.Contains(path);

        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive)
        {
            string prefix = EnsureTrailingSeparator(path);
            return _files
                .Where(f => recursive
                    ? f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    : ParentOf(f).Equals(path, StringComparison.OrdinalIgnoreCase))
                .Where(f => searchPattern == "*" || Path.GetFileName(f).EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public void CopyFile(string sourcePath, string destinationPath) { }

        public void DeleteFile(string path) => _files.Remove(path);

        public void MoveFile(string sourcePath, string destinationPath)
        {
            if (!_files.Remove(sourcePath))
            {
                throw new IOException($"Source file not found: {sourcePath}");
            }

            if (_files.Contains(destinationPath))
            {
                _files.Add(sourcePath);
                throw new IOException($"Destination already exists: {destinationPath}");
            }

            _files.Add(destinationPath);
        }

        public string ReadAllText(string path) => string.Empty;

        public void WriteAllText(string path, string contents) { }

        public void CreateDirectory(string path) => _directories.Add(path);

        public void CopyDirectory(string sourcePath, string destinationPath) { }

        public void DeleteDirectory(string path, bool recursive) => _directories.Remove(path);

        public void MoveDirectory(string sourcePath, string destinationPath) { }

        private static string EnsureTrailingSeparator(string path) =>
            path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

        private static string ParentOf(string filePath) => Path.GetDirectoryName(filePath) ?? string.Empty;
    }

    private sealed class FakeDataDirectoryProvider : IDataDirectoryProvider
    {
        public string Current => @"D:\data";

        public void Initialize() { }

        public string Resolve(Settings settings) => Current;

        public void MoveTo(string directory, Settings settings) { }
    }

    private sealed class FakeServerProcessState : IDayZServerProcessState
    {
        public bool Running { get; set; }

        public bool IsDayZServerRunning() => Running;
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;

        public int ConfirmCalls { get; private set; }

        public string? LastConfirmMessage { get; private set; }

        public int ConfirmWithWarningCalls { get; private set; }

        public int ShowWarningCalls { get; private set; }

        public string? LastWarningMessage { get; private set; }

        public string? LastWarningNote { get; private set; }

        public IReadOnlyList<string>? SelectedFiles { get; set; }

        public string? LastModFolderPath { get; private set; }

        public int PickTypeFilesCalls { get; private set; }

        public int ShowMessageCalls { get; private set; }

        public string? LastMessage { get; private set; }

        public string? LastMessageTitle { get; private set; }

        public string? AskTextResult { get; set; } = null;

        public int AskTextCalls { get; private set; }

        public string? LastErrorMessage { get; private set; }

        public string? LastErrorTitle { get; private set; }

        public void ShowMessage(string message, string title, bool isError = false)
        {
            ShowMessageCalls++;
            LastMessage = message;
            LastMessageTitle = title;

            if (isError)
            {
                LastErrorMessage = message;
                LastErrorTitle = title;
            }
        }

        public bool Confirm(string message, string title)
        {
            ConfirmCalls++;
            LastConfirmMessage = message;
            return ConfirmResult;
        }

        public bool ConfirmWithWarning(string message, string title, string warning, string note = "")
        {
            ConfirmWithWarningCalls++;
            LastWarningMessage = warning;
            LastWarningNote = note;
            return ConfirmResult;
        }

    public void ShowWarning(string message, string title, string warning, string note = "")
    {
        ShowWarningCalls++;
        LastWarningMessage = warning;
        LastWarningNote = note;
    }

    public int ShowAlertCalls { get; private set; }

    public string? LastAlertMessage { get; private set; }

    public string? LastAlertWarning { get; private set; }

    public string? LastAlertNote { get; private set; }

    public void ShowAlert(string message, string title, string warning, string note = "")
    {
        ShowAlertCalls++;
        LastAlertMessage = message;
        LastAlertWarning = warning;
        LastAlertNote = note;
    }

        public string? AskText(string title, string prompt, string defaultValue = "")
        {
            AskTextCalls++;
            return AskTextResult;
        }

        public string? PickFolder(string title = "Select a folder") => null;

        public string? PickFile(string title, string filter, string initialDirectory) => null;

        public IReadOnlyList<string>? PickTypeFiles(
            string modName, IReadOnlyList<string> files, string? modFolderPath = null)
        {
            PickTypeFilesCalls++;
            LastModFolderPath = modFolderPath;
            return SelectedFiles;
        }

        public AddPresetRequest? AskAddPresetResult { get; set; }

        public AddPresetRequest? AskAddPreset(string mapName) => AskAddPresetResult;

        public DuplicatePresetRequest? AskDuplicatePresetResult { get; set; }

        public IReadOnlyList<string>? AskDuplicatePresetMaps { get; private set; }

        public string? AskDuplicatePresetDefaultMap { get; private set; }

        public DuplicatePresetRequest? AskDuplicatePreset(
            string sourcePresetName, IReadOnlyList<string> maps, string defaultMap)
        {
            AskDuplicatePresetMaps = maps;
            AskDuplicatePresetDefaultMap = defaultMap;
            return AskDuplicatePresetResult;
        }
    }
}

