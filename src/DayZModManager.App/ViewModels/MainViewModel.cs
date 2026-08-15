using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.App.Services;

namespace DayZModManager.App.ViewModels;

/// <summary>
/// Top-level view model. Orchestrates settings/mods/maps/types state, exposes the
/// footer status and the Apply / Start Server actions, and owns the log.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IModOrderStore _modOrderStore;
    private readonly ITypesConfigStore _typesConfigStore;
    private readonly IApplyService _applyService;
    private readonly IProcessLauncher _launcher;
    private readonly string _dataDirectory;

    private Settings _settings = new();
    private bool _isDirty;
    private string _statusText = "Configuration applied";

    public MainViewModel(
        ISettingsService settingsService,
        IModOrderStore modOrderStore,
        ITypesConfigStore typesConfigStore,
        IApplyService applyService,
        IModDiscoveryService discoveryService,
        IMapService mapService,
        ITypesService typesService,
        IServerConfigService serverConfigService,
        IBatchFileService batchFileService,
        IFileSystem fileSystem,
        IDialogService dialogs,
        IProcessLauncher launcher,
        string dataDirectory)
    {
        _settingsService = settingsService;
        _modOrderStore = modOrderStore;
        _typesConfigStore = typesConfigStore;
        _applyService = applyService;
        _launcher = launcher;
        _dataDirectory = dataDirectory;

        Log = new LogViewModel();
        ModState = new ModState();
        TypesConfig = new TypesConfig();

        // --- Load persistent state ---
        _settings = LoadOrCreateSettings();

        ConfigLoadResult<IReadOnlyList<string>> order = _modOrderStore.Load(_dataDirectory);
        if (order.Status == ConfigLoadStatus.Success)
        {
            ModState.ReplaceLoadedMods(order.Value!);
        }

        ConfigLoadResult<TypesConfig> types = _typesConfigStore.Load(_dataDirectory);
        if (types.Status == ConfigLoadStatus.Success)
        {
            ApplyLoadedTypes(types.Value!);
        }

        // --- Build child view models ---
        Mods = new ModsViewModel(ModState, discoveryService, Log);
        MapTypes = new MapTypesViewModel(
            mapService, typesService, typesConfigStore, serverConfigService, batchFileService,
            fileSystem, dialogs, Log, TypesConfig, _dataDirectory);
        MapTypes.EnsureApplied = EnsureApplied;
        Settings = new SettingsViewModel(dialogs);
        Settings.ApplyRequested += async () => await ApplyAsync();

        ApplyCommand = new AsyncRelayCommand(async () => await ApplyAsync());
        StartServerCommand = new AsyncRelayCommand(StartServerAsync);

        Mods.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModsViewModel.IsDirty))
            {
                UpdateDirty();
            }
        };
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsDirty))
            {
                UpdateDirty();
            }
        };

        // --- Initial setup ---
        Settings.Load(_settings);
        Mods.Initialize();

        UpdateDirty();
    }

    /// <summary>
    /// Performs the initial discovery and reconciliation off the UI thread so
    /// startup does not block on directory enumeration.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            await Mods.RefreshAsync(_settings.WorkshopPath);
            MapTypes.Refresh(_settings, ModState.WorkshopMods.ToList(), ModState.LoadedMods.ToList());
            Mods.MarkApplied();
            UpdateDirty();
        }
        catch (Exception ex)
        {
            Log.Error($"Initialization failed: {ex.Message}");
        }
    }

    public LogViewModel Log { get; }

    public ModState ModState { get; }

    public TypesConfig TypesConfig { get; }

    public ModsViewModel Mods { get; }

    public MapTypesViewModel MapTypes { get; }

    public SettingsViewModel Settings { get; }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetField(ref _isDirty, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand StartServerCommand { get; }

    /// <summary>Refreshes the Map &amp; Types mod dropdown from the current in-memory loaded mods.</summary>
    public void OnMapTypesTabActivated() => MapTypes.Sync(ModState.WorkshopMods, ModState.LoadedMods);

    private Settings LoadOrCreateSettings()
    {
        ConfigLoadResult<Settings> result = _settingsService.Load(_dataDirectory);

        if (result.Status != ConfigLoadStatus.Success)
        {
            Log.Warning("No configuration found. Set the server and workshop paths in the Settings tab.");
            var defaults = new Settings();
            _settingsService.Save(_dataDirectory, defaults);
            return defaults;
        }

        return result.Value!;
    }

    private void ApplyLoadedTypes(TypesConfig loaded)
    {
        TypesConfig.Maps.Clear();
        foreach (KeyValuePair<string, MapTypesConfig> pair in loaded.Maps)
        {
            TypesConfig.Maps[pair.Key] = pair.Value;
        }

        TypesConfig.CurrentMap = loaded.CurrentMap;
    }

    private async Task<bool> ApplyAsync()
    {
        Settings newSettings = Settings.ToSettings();
        IReadOnlyList<string> loadedMods = ModState.LoadedMods.ToList();

        ApplyResult result = await Task.Run(() => _applyService.Apply(new ApplyContext
        {
            Settings = newSettings,
            LoadedMods = loadedMods,
            DataDirectory = _dataDirectory,
        }));

        foreach (string line in result.Logs)
        {
            Log.Info(line);
        }

        if (!result.Success)
        {
            Log.Error("Apply failed.");
            return false;
        }

        bool pathChanged = newSettings.WorkshopPath != _settings.WorkshopPath
            || newSettings.ServerPath != _settings.ServerPath;

        _settings = newSettings;
        Settings.MarkApplied(newSettings);
        Mods.MarkApplied();

        if (pathChanged)
        {
            await Mods.RefreshAsync(newSettings.WorkshopPath);
            MapTypes.Refresh(newSettings, ModState.WorkshopMods.ToList(), ModState.LoadedMods.ToList());
        }
        else
        {
            MapTypes.Sync(ModState.WorkshopMods, ModState.LoadedMods);
        }

        MapTypes.SyncEconomyCore();
        UpdateDirty();
        return true;
    }

    private async Task StartServerAsync()
    {
        if (IsDirty && !await ApplyAsync())
        {
            return;
        }

        try
        {
            _launcher.Launch(_settings.BatFilePath, _settings.ServerPath);
            Log.Success("Server launch initiated.");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to launch server: {ex.Message}");
        }
    }

    private void UpdateDirty()
    {
        bool dirty = Mods.IsDirty || Settings.IsDirty;
        IsDirty = dirty;
        StatusText = dirty ? "\u25CF Unsaved changes" : "\u2713 Configuration applied";
    }

    private Task<bool> EnsureApplied() => !IsDirty ? Task.FromResult(true) : ApplyAsync();
}
