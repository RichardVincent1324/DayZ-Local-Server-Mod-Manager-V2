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
        ITypesConfigStore? typesConfigStore = null,
        FakeTypesService? typesService = null,
        FakeDialogs? dialogs = null,
        FakeSaveGameService? saveGameService = null,
        FakeServerProcessState? serverProcess = null,
        FakeFileSystem? fileSystem = null,
        FakeBatchFileService? batchFileService = null,
        FakeProcessLauncher? processLauncher = null,
        FakePresetService? presetService = null,
        IJunctionService? junctionService = null)
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

        return new PresetTypesViewModel(
            mapService,
            typesService,
            saveGameService,
            typesConfigStore,
            new FakeServerConfigService(),
            batchFileService,
            fileSystem,
            dialogs,
            new LogViewModel(),
            config,
            new FakeDataDirectoryProvider(),
            serverProcess,
            processLauncher,
            presetService,
            junctionService);
    }

    private static string PresetFolder(string mapName) =>
        Path.Combine(@"D:\data", "Presets", mapName, "__default_preset__");

    private static string PresetModTypesFolder(string mapName) =>
        Path.Combine(PresetFolder(mapName), "ModTypes");

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
        FakeTypesConfigStore? typesConfigStore = null,
        FakeProcessLauncher? processLauncher = null)
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
            processLauncher: processLauncher);
        vm.Refresh(Settings(), workshopMods, loadedMods);
        return (vm, types, dialogs);
    }

    private static void SelectSave(PresetTypesViewModel vm, string name) =>
        vm.SelectedSave = vm.SaveNames.First(entry => entry.Name == name && !entry.IsOrphaned);

    private static ModTypesEntry Entry(string modName, params string[] generatedFiles) =>
        new() { ModName = modName, GeneratedFiles = generatedFiles.ToList() };

    private static TypesConfig ConfigWithEntry(ModTypesEntry entry) =>
        new()
        {
            CurrentMap = MapName,
            Maps = { [MapName] = new MapTypesConfig { Mods = { entry } } },
        };

    private static TypesConfig AppliedConfig() =>
        new() { CurrentMap = MapName, Maps = { [MapName] = new MapTypesConfig() } };

    private static string ModTypesFolderPath() =>
        PresetModTypesFolder(MapName);

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

        public IReadOnlyList<string>? ConfigureModSourceFiles { get; private set; }

        public IReadOnlyList<TypeFileSelection>? ConfigureModSelections { get; private set; }

        public int ConfigureModCalls { get; private set; }

        public IReadOnlySet<string>? LastRemoveLeaves { get; private set; }

        public int RemoveFilesCalls { get; private set; }

        public IReadOnlySet<string>? LastCleanValidMods { get; private set; }

        public int CleanInvalidCalls { get; private set; }

        public int RemoveUntrackedCalls { get; private set; }

        public IReadOnlySet<string>? LastUntrackedLeaves { get; private set; }

        public IReadOnlyList<string> DiscoverXmlFiles(string workshopPath, string modName) => DiscoveryFiles;

        public IReadOnlyList<ConfiguredTypeFile> GetConfiguredFiles(TypesConfig config, string mapName, string modName)
        {
            var files = new List<ConfiguredTypeFile>();
            if (!config.Maps.TryGetValue(mapName, out MapTypesConfig? map))
            {
                return files;
            }

            ModTypesEntry? entry = map.Mods.FirstOrDefault(m =>
                string.Equals(m.ModName, modName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return files;
            }

            for (int i = 0; i < entry.GeneratedFiles.Count; i++)
            {
                string generated = entry.GeneratedFiles[i];
                string leaf = Path.GetFileName(generated);
                if (string.IsNullOrEmpty(leaf))
                {
                    continue;
                }

                string source = i < entry.SourceFiles.Count ? entry.SourceFiles[i] : string.Empty;
                TypesFileRole role = entry.FileRoles.TryGetValue(leaf, out string? stored)
                    ? TypesFileRoles.ToRole(stored)
                    : TypesFileRole.Types;
                files.Add(new ConfiguredTypeFile(source, leaf, role));
            }

            return files;
        }

        public TypesOperationResult ConfigureMod(
            TypesConfig config, string mapName, TypesTarget target, string workshopPath, string modName,
            IReadOnlyList<TypeFileSelection> selections, IReadOnlySet<string> loadedModNames)
        {
            ConfigureModCalls++;
            ConfigureModSelections = selections.ToList();
            ConfigureModSourceFiles = selections.Select(s => s.SourceFile).ToList();
            LastTarget = target;
            return new TypesOperationResult { Success = true };
        }

        public TypesOperationResult RemoveFiles(
            TypesConfig config, string mapName, TypesTarget target, string modName,
            IReadOnlySet<string> fileLeaves, IReadOnlySet<string> loadedModNames)
        {
            RemoveFilesCalls++;
            LastRemoveLeaves = fileLeaves;
            LastTarget = target;
            return new TypesOperationResult { Success = true };
        }

        public TypesOperationResult CleanInvalid(
            TypesConfig config, string mapName, TypesTarget target,
            IReadOnlySet<string> validModNames, IReadOnlySet<string> loadedModNames)
        {
            CleanInvalidCalls++;
            LastCleanValidMods = validModNames;
            LastTarget = target;
            return new TypesOperationResult { Success = true };
        }

        public TypesOperationResult RemoveUntrackedFiles(
            TypesConfig config, string mapName, TypesTarget target, IReadOnlySet<string> fileLeaves)
        {
            RemoveUntrackedCalls++;
            LastUntrackedLeaves = fileLeaves;
            LastTarget = target;
            return new TypesOperationResult { Success = true };
        }

        public TypesTarget? LastTarget { get; private set; }

        public int SyncEconomyCoreCalls { get; private set; }

        public IReadOnlySet<string>? LastPreviouslyOwned { get; private set; }

        public string? LastEconomyFolder { get; private set; }

        public MapTypesConfig? LastEconomyMap { get; private set; }

        public bool SyncEconomyCore(MapTypesConfig? map, string missionPath, string folder, IReadOnlySet<string> loadedModNames, IReadOnlySet<string>? previouslyOwned = null)
        {
            SyncEconomyCoreCalls++;
            LastEconomyMap = map;
            LastEconomyFolder = folder;
            LastPreviouslyOwned = previouslyOwned;
            return true;
        }

        public string? ActiveTypesFolder { get; set; }

        public string? GetActiveTypesFolder(string missionPath) => ActiveTypesFolder;

        public IReadOnlyList<string> ActiveTypeFiles { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> GetActiveTypeFileNames(
            TypesConfig config, string mapName, IReadOnlySet<string> loadedModNames) => ActiveTypeFiles;
    }

    private sealed class FakeTypesConfigStore : ITypesConfigStore
    {
        public bool SaveCalled { get; private set; }

        public ConfigLoadResult<TypesConfig> Load(string dataDirectory) => ConfigLoadResult<TypesConfig>.Missing();

        public void Save(string dataDirectory, TypesConfig config) => SaveCalled = true;
    }

    private sealed class FakeServerConfigService : IServerConfigService
    {
        public bool UpdateTemplate(string serverPath, string missionFolderName) => true;

        public bool WriteInstanceId(string serverConfigPath, int instanceId) => true;
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

        public List<(string Name, bool CopyProfiles)> Created { get; } = new();

        public List<(string Source, string NewName, bool CopyProfiles)> Duplicated { get; } = new();

        public List<(string OldName, string NewName)> Renamed { get; } = new();

        public List<string> Deleted { get; } = new();

        public bool IsDefaultPreset(string? presetName) => PresetPaths.IsDefaultPreset(presetName);

        public string GetPresetFolder(string dataDirectory, string mapName, string presetName) =>
            PresetPaths.PresetFolder(dataDirectory, mapName, presetName);

        public bool PresetExists(string dataDirectory, string mapName, string presetName) =>
            PresetNames.Any(n => string.Equals(n, presetName, StringComparison.Ordinal));

        public IReadOnlyList<string> ListPresetNames(string dataDirectory, string mapName) =>
            PresetNames.ToList();

        public int ReadInstanceId(string dataDirectory, string mapName, string presetName) => 1;

        public int AllocateInstanceId(string dataDirectory) => 1;

        public PresetResult EnsureDefaultPreset(string serverPath, string dataDirectory, string mapName)
        {
            if (!PresetNames.Any(PresetPaths.IsDefaultPreset))
            {
                PresetNames.Insert(0, PresetPaths.DefaultPresetName);
            }

            return new() { Success = true };
        }

        public PresetResult CreatePreset(
            string serverPath, string dataDirectory, string mapName, string presetName, bool copyProfilesFromDefault)
        {
            if (PresetNames.Any(n => string.Equals(n, presetName, StringComparison.OrdinalIgnoreCase)))
            {
                return new() { Success = false, Message = $"A preset named \"{presetName}\" already exists." };
            }

            PresetNames.Add(presetName);
            Created.Add((presetName, copyProfilesFromDefault));
            return new() { Success = true, Message = $"Created \"{presetName}\"." };
        }

        public PresetResult DuplicatePreset(
            string serverPath, string dataDirectory, string mapName,
            string sourcePresetName, string newPresetName, bool copyProfiles)
        {
            if (PresetNames.Any(n => string.Equals(n, newPresetName, StringComparison.OrdinalIgnoreCase)))
            {
                return new() { Success = false, Message = $"A preset named \"{newPresetName}\" already exists." };
            }

            PresetNames.Add(newPresetName);
            Duplicated.Add((sourcePresetName, newPresetName, copyProfiles));
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

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path) => _files.Add(path);

        public bool DirectoryExists(string path) => _directories.Contains(path);

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

        public IReadOnlyList<TypeFileSelection>? SelectedFiles { get; set; }

        public IReadOnlySet<string>? LastActiveFiles { get; private set; }

        public IReadOnlyDictionary<string, TypesFileRole>? LastActiveRoles { get; private set; }

        public string? LastModFolderPath { get; private set; }

        public string? AskTextResult { get; set; } = null;

        public string? LastErrorMessage { get; private set; }

        public string? LastErrorTitle { get; private set; }

        public void ShowMessage(string message, string title, bool isError = false)
        {
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

        public string? AskText(string title, string prompt, string defaultValue = "") => AskTextResult;

        public string? PickFolder(string title = "Select a folder") => null;

        public string? PickFile(string title, string filter, string initialDirectory) => null;

        public IReadOnlyList<TypeFileSelection>? PickTypeFiles(
            string modName, IReadOnlyList<string> files, IReadOnlySet<string>? activeFiles = null,
            IReadOnlyDictionary<string, TypesFileRole>? activeRoles = null, string? modFolderPath = null)
        {
            LastActiveFiles = activeFiles;
            LastActiveRoles = activeRoles;
            LastModFolderPath = modFolderPath;
            return SelectedFiles;
        }

        public AddPresetRequest? AskAddPresetResult { get; set; }

        public AddPresetRequest? AskAddPreset(string mapName) => AskAddPresetResult;

        public DuplicatePresetRequest? AskDuplicatePresetResult { get; set; }

        public DuplicatePresetRequest? AskDuplicatePreset(string sourcePresetName) => AskDuplicatePresetResult;
    }
}

