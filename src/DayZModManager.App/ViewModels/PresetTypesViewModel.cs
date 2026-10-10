using System.Collections.ObjectModel;
using System.IO;
using DayZModManager.Core;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.App.Services;

namespace DayZModManager.App.ViewModels;

/// <summary>
/// Backs the "Preset & Types" page. Map selection and types configuration are
/// applied immediately, not deferred to the main Apply.
/// </summary>
public sealed partial class PresetTypesViewModel : ViewModelBase
{
    /// <summary>Mod-name label shown for files in type_files that are not tracked by the config.</summary>
    private const string UntrackedLabel = "(untracked)";
    private readonly IMapService _mapService;
    private readonly ITypesService _typesService;
    private readonly ISaveGameService _saveGameService;
    private readonly ITypesConfigStore _typesConfigStore;
    private readonly IServerConfigService _serverConfig;
    private readonly IBatchFileService _batchFile;
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogs;
    private readonly LogViewModel _log;
    private readonly TypesConfig _typesConfig;
    private readonly IDataDirectoryProvider _dataDirectoryProvider;
    private readonly IDayZServerProcessState _serverProcess;
    private readonly IProcessLauncher _processLauncher;
    private readonly IPresetService _presetService;
    private readonly IJunctionService _junctions;
    private string _serverPath = string.Empty;
    private string _workshopPath = string.Empty;
    private string _batFileName = string.Empty;
    private string _activePresetName = PresetPaths.DefaultPresetName;
    private List<string> _allMods = new();
    private IReadOnlyList<string> _loadedMods = Array.Empty<string>();
    private IReadOnlyList<MapInfo> _discoveredMaps = Array.Empty<MapInfo>();
    private string? _selectedMap;
    private string? _selectedMod;
    private SaveListEntryViewModel? _selectedSave;
    private bool _isRestoring;
    private bool _isSwitching;
    private bool _typesBusy;
    private bool _isSaveBusy;
    private bool _isSwitchingPreset;
    private bool _isRestoringPreset;
    private bool _isRestoringMod;
    private string? _pendingMap;

    public PresetTypesViewModel(
        IMapService mapService,
        ITypesService typesService,
        ISaveGameService saveGameService,
        ITypesConfigStore typesConfigStore,
        IServerConfigService serverConfig,
        IBatchFileService batchFile,
        IFileSystem fileSystem,
        IDialogService dialogs,
        LogViewModel log,
        TypesConfig typesConfig,
        IDataDirectoryProvider dataDirectoryProvider,
        IDayZServerProcessState serverProcess,
        IProcessLauncher processLauncher,
        IPresetService presetService,
        IJunctionService junctions)
    {
        _mapService = mapService;
        _typesService = typesService;
        _saveGameService = saveGameService;
        _typesConfigStore = typesConfigStore;
        _serverConfig = serverConfig;
        _batchFile = batchFile;
        _fileSystem = fileSystem;
        _dialogs = dialogs;
        _log = log;
        _typesConfig = typesConfig;
        _dataDirectoryProvider = dataDirectoryProvider;
        _serverProcess = serverProcess;
        _processLauncher = processLauncher;
        _presetService = presetService;
        _junctions = junctions;

        ConfigureModCommand = new RelayCommand<string>(ConfigureMod, () => MapApplied);
        OpenTypeFilesFolderCommand = new RelayCommand(OpenTypeFilesFolder, () => MapApplied);
        OpenMapProfilesFolderCommand = new RelayCommand(OpenMapProfilesFolder);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => MapApplied && CanRemoveSelected);
        CleanInvalidCommand = new RelayCommand(CleanInvalid, () => MapApplied);
        LoadSaveCommand = new RelayCommand(LoadSave, () => SelectedSave is { IsOrphaned: false } && !IsSaveBusy);
        DeleteSaveCommand = new RelayCommand(DeleteSave, () => SelectedSave is not null && !IsSaveBusy);
        RenameSaveCommand = new RelayCommand(RenameSave, () => SelectedSave is { IsOrphaned: false } && !IsSaveBusy);
        AddSaveCommand = new RelayCommand(AddSave, () => !IsSaveBusy);
        WipeWorldCommand = new RelayCommand(WipeWorld, () => !IsSaveBusy);
        AddPresetCommand = new RelayCommand(AddPreset, () => !IsBusy && !string.IsNullOrEmpty(_typesConfig.CurrentMap));
        DuplicatePresetCommand = new RelayCommand(
            DuplicatePreset, () => !IsBusy && !string.IsNullOrEmpty(_typesConfig.CurrentMap) && SelectedPreset is not null);
        RenamePresetCommand = new RelayCommand(RenamePreset, () => SelectedPreset is { IsDefault: false } && !IsBusy);
        DeletePresetCommand = new RelayCommand(DeletePreset, () => SelectedPreset is { IsDefault: false } && !IsBusy);

        SelectedTypesRows.CollectionChanged += (_, _) => NotifyCommandStates();
    }

    /// <summary>
    /// The preset whose environment (types, mod order, profiles, instance ID) this
    /// page operates on. Set by the shell when the active preset changes. Defaults
    /// to the map's reserved default preset.
    /// </summary>
    public string ActivePresetName
    {
        get => _activePresetName;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? PresetPaths.DefaultPresetName : value;
            if (SetField(ref _activePresetName, normalized))
            {
                RebuildRows();
                RefreshSaves();
                NotifyCommandStates();
            }
        }
    }

    /// <summary>Folder of the active preset for the given map.</summary>
    private string PresetFolder(string mapName) =>
        PresetPaths.PresetFolder(_dataDirectoryProvider.Current, mapName, _activePresetName);

    /// <summary>Preset type_files folder for the given map.</summary>
    private string PresetTypeFilesFolder(string mapName) =>
        PresetPaths.TypeFilesFolder(_dataDirectoryProvider.Current, mapName, _activePresetName);

    /// <summary>Preset saves folder for the given map.</summary>
    private string PresetSavesFolder(string mapName) =>
        PresetPaths.SavesFolder(_dataDirectoryProvider.Current, mapName, _activePresetName);

    /// <summary>The active preset's dedicated instance ID for the given map.</summary>
    private int PresetInstanceId(string mapName) =>
        _presetService.ReadInstanceId(_dataDirectoryProvider.Current, mapName, _activePresetName);

    /// <summary>
    /// Invoked when the user selects a different preset. Loads the preset's mod
    /// order and types configuration, persists it as the map's active preset, and
    /// applies it. Provided by the shell. Returns false on failure.
    /// </summary>
    public Func<string, string, Task<bool>>? ActivatePreset { get; set; }

    /// <summary>Invoked to resolve the persisted active preset for a map. Provided by the shell.</summary>
    public Func<string, string>? ResolvePresetForMap { get; set; }

    /// <summary>
    /// Invoked after a map and its active preset have been successfully applied so
    /// the shell can persist the selection. Provided by the shell.
    /// </summary>
    public Action<string, string>? PersistActiveSelection { get; set; }

    /// <summary>The presets available for the current map.</summary>
    public ObservableCollection<PresetItemViewModel> Presets { get; } = new();

    private PresetItemViewModel? _selectedPreset;

    /// <summary>
    /// The selected preset. Selecting a different preset makes it the map's active
    /// preset (loading its configuration and applying it). Programmatic restoration
    /// is flagged so it never re-activates.
    /// </summary>
    public PresetItemViewModel? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!SetField(ref _selectedPreset, value))
            {
                return;
            }

            DuplicatePresetCommand.RaiseCanExecuteChanged();

            if (!_isRestoringPreset
                && value is not null
                && !string.Equals(value.Name, _activePresetName, StringComparison.Ordinal))
            {
                _ = SwitchPresetAsync(value.Name);
            }
        }
    }

    public ObservableCollection<string> MapNames { get; } = new();

    public ObservableCollection<string> ModNames { get; } = new();

    public ObservableCollection<TypesRowViewModel> TypesRows { get; } = new();

    public ObservableCollection<TypesRowViewModel> SelectedTypesRows { get; } = new();

    /// <summary>
    /// The currently selected map. Selecting a different map applies it
    /// immediately (server template, the active preset's profiles and config, and economy).
    /// Programmatic restoration during refresh is flagged so it never re-applies.
    /// </summary>
    public string? SelectedMap
    {
        get => _selectedMap;
        set
        {
            if (SetField(ref _selectedMap, value) && !_isRestoring)
            {
                RequestMapSwitch(value);
            }
        }
    }

    /// <summary>
    /// The mod selected in the Types Config dropdown. Selecting a mod runs the
    /// configure flow and then clears the selection, so the dropdown behaves as an
    /// action (like the Current Map dropdown) and the same mod can be re-selected.
    /// </summary>
    public string? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (!SetField(ref _selectedMod, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsModSelectionEmpty));

            if (_isRestoringMod || string.IsNullOrEmpty(value))
            {
                return;
            }

            HandleModSelection(value);
        }
    }

    /// <summary>True when no mod is selected; drives the Types Config placeholder overlay.</summary>
    public bool IsModSelectionEmpty => string.IsNullOrEmpty(_selectedMod);

    public SaveListEntryViewModel? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (SetField(ref _selectedSave, value))
            {
                LoadSaveCommand.RaiseCanExecuteChanged();
                DeleteSaveCommand.RaiseCanExecuteChanged();
                RenameSaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True while a save operation is running in the background.</summary>
    public bool IsSaveBusy
    {
        get => _isSaveBusy;
        private set
        {
            if (SetField(ref _isSaveBusy, value))
            {
                NotifyBusyChanged();
                LoadSaveCommand.RaiseCanExecuteChanged();
                DeleteSaveCommand.RaiseCanExecuteChanged();
                AddSaveCommand.RaiseCanExecuteChanged();
                WipeWorldCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Stored progress saves for the current map, plus any unattached
    /// (orphaned) live storage folders that no preset owns.
    /// </summary>
    public ObservableCollection<SaveListEntryViewModel> SaveNames { get; } = new();

    private bool CanRemoveSelected => SelectedTypesRows.Count > 0;

    /// <summary>True when a map is applied (types operations require a map).</summary>
    private bool MapApplied => !string.IsNullOrWhiteSpace(_typesConfig.CurrentMap);

    /// <summary>Tooltip for the type_files folder button.</summary>
    public string OpenTypeFilesFolderToolTip => "Open type_files folder in File Explorer";

    /// <summary>Tooltip for the always-available preset profiles folder button.</summary>
    public string MapProfilesFolderToolTip => "Open the preset profiles folder in File Explorer";

    /// <summary>
    /// True while a map switch, a types operation (configure/remove/clean), or a
    /// progress-save operation (load/add/delete/wipe world) is in flight. Used by the
    /// Start Server action so it never launches the server while the launch batch,
    /// mission files, or the live storage folder are being rewritten or deleted.
    /// </summary>
    public bool IsBusy => _isSwitching || _typesBusy || _isSaveBusy || _isSwitchingPreset;

    private void NotifyBusyChanged() => OnPropertyChanged(nameof(IsBusy));

    /// <summary>
    /// Invoked before configuring types. Returns true when the current in-memory
    /// state is persisted (either already or just applied), false if Apply failed.
    /// </summary>
    public Func<Task<bool>>? EnsureApplied { get; set; }

    public RelayCommand<string> ConfigureModCommand { get; }

    public RelayCommand OpenTypeFilesFolderCommand { get; }

    public RelayCommand OpenMapProfilesFolderCommand { get; }

    public RelayCommand RemoveSelectedCommand { get; }

    public RelayCommand CleanInvalidCommand { get; }

    public RelayCommand LoadSaveCommand { get; }

    public RelayCommand AddSaveCommand { get; }

    public RelayCommand DeleteSaveCommand { get; }

    public RelayCommand RenameSaveCommand { get; }

    public RelayCommand WipeWorldCommand { get; }

    public RelayCommand AddPresetCommand { get; }

    public RelayCommand DuplicatePresetCommand { get; }

    public RelayCommand RenamePresetCommand { get; }

    public RelayCommand DeletePresetCommand { get; }

    public void Refresh(Settings settings, IReadOnlyList<string> workshopMods, IReadOnlyList<string> loadedMods)
    {
        _serverPath = settings.ServerPath;
        _workshopPath = settings.WorkshopPath;
        _batFileName = settings.BatFileName;
        Sync(workshopMods, loadedMods);
    }

    /// <summary>Refreshes maps, the mod dropdown and the grid (used on tab activation).</summary>
    public void Sync(IReadOnlyList<string> workshopMods, IReadOnlyList<string> loadedMods)
    {
        _allMods = workshopMods.ToList();
        _loadedMods = loadedMods.ToList();

        RebuildMapNames();
        RefreshModNames(loadedMods);
        RebuildRows();
        RefreshPresets();
        RefreshSaves();
        NotifyCommandStates();
    }

    /// <summary>Returns the applied map name, warning when none is applied yet.</summary>
    private string? AppliedMapOrWarn()
    {
        if (string.IsNullOrWhiteSpace(_typesConfig.CurrentMap))
        {            _log.Warning("Apply a map first before managing progress saves.");
            return null;
        }

        return _typesConfig.CurrentMap;
    }

    /// <summary>
    /// Refuses a progress-save operation while the DayZ server is running, since
    /// it may be writing to the live storage folder. Shows a modal error plus a log
    /// line and returns true when the operation was blocked.
    /// </summary>
    private bool RefuseWhileServerRunning(string purpose)
    {
        if (!_serverProcess.IsDayZServerRunning())
        {
            return false;
        }

        string message = $"The DayZ server is running. Stop it before {purpose} to avoid corrupting the saved progress data.";
        _log.Error(message);
        _dialogs.ShowMessage(message, "DayZ Server running", isError: true);
        return true;
    }

    private void NotifyCommandStates()
    {
        ConfigureModCommand.RaiseCanExecuteChanged();
        OpenTypeFilesFolderCommand.RaiseCanExecuteChanged();
        RemoveSelectedCommand.RaiseCanExecuteChanged();
        CleanInvalidCommand.RaiseCanExecuteChanged();
        RenameSaveCommand.RaiseCanExecuteChanged();
        AddPresetCommand.RaiseCanExecuteChanged();
        DuplicatePresetCommand.RaiseCanExecuteChanged();
        RenamePresetCommand.RaiseCanExecuteChanged();
        DeletePresetCommand.RaiseCanExecuteChanged();
    }

    private async Task<bool> EnsureAppliedBeforeAsync(string action)
    {
        if (EnsureApplied is not null && !await EnsureApplied())
        {
            _log.Error($"Apply failed; {action} aborted.");
            return false;
        }

        return true;
    }

    private static bool ContainsIgnoreCase(ObservableCollection<string> items, string value) =>
        items.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
}

