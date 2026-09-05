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
    private readonly IDataDirectoryProvider _dataDirectoryProvider;
    private readonly IDialogService _dialogs;

    private Settings _settings = new();
    private bool _isDirty;
    private string _statusText = "Configuration applied";
    private Task<bool>? _pendingApply;

    private bool _relocatingDataDirectory;

    public MainViewModel(
        ISettingsService settingsService,
        IModOrderStore modOrderStore,
        ITypesConfigStore typesConfigStore,
        IApplyService applyService,
        IModDiscoveryService discoveryService,
        IMapService mapService,
        ITypesService typesService,
        ISaveGameService saveGameService,
        IServerConfigService serverConfigService,
        IBatchFileService batchFileService,
        IFileSystem fileSystem,
        IDialogService dialogs,
        IProcessLauncher launcher,
        IDataDirectoryProvider dataDirectoryProvider)
    {
        _settingsService = settingsService;
        _modOrderStore = modOrderStore;
        _typesConfigStore = typesConfigStore;
        _applyService = applyService;
        _launcher = launcher;
        _dataDirectoryProvider = dataDirectoryProvider;
        _dialogs = dialogs;

        Log = new LogViewModel();
        ModState = new ModState();
        TypesConfig = new TypesConfig();

        // --- Load persistent state ---
        _settings = LoadOrCreateSettings();

        ConfigLoadResult<IReadOnlyList<string>> order = _modOrderStore.Load(_dataDirectoryProvider.Current);
        if (order.Status == ConfigLoadStatus.Success)
        {
            ModState.ReplaceLoadedMods(order.Value!);
        }
        else if (order.Status == ConfigLoadStatus.Corrupt)
        {
            Log.Error("mod_order.json is corrupt and was ignored.");
        }

        ConfigLoadResult<TypesConfig> types = _typesConfigStore.Load(_dataDirectoryProvider.Current);
        if (types.Status == ConfigLoadStatus.Success)
        {
            ApplyLoadedTypes(types.Value!);
        }
        else if (types.Status == ConfigLoadStatus.Corrupt)
        {
            Log.Error("types_config.json is corrupt and was ignored.");
        }

        // --- Build child view models ---
        Mods = new ModsViewModel(ModState, discoveryService, Log);
        MapTypes = new MapTypesViewModel(
            mapService, typesService, saveGameService, typesConfigStore, serverConfigService, batchFileService,
            fileSystem, dialogs, Log, TypesConfig, _dataDirectoryProvider);
        MapTypes.EnsureApplied = EnsureApplied;
        Settings = new SettingsViewModel(dialogs);
        Settings.ApplyRequested += async () => await ApplyAsync();
        Settings.DataDirectoryChanged += HandleDataDirectoryChanged;

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
            Settings startupSettings = _settings;
            await Mods.RefreshAsync(startupSettings.WorkshopPath);

            // Coordinate with any Apply that started while discovery was running
            // (e.g. the user picked paths from the Settings tab). Waiting here
            // prevents two actors from rewriting the same batch/config files.
            if (_pendingApply is not null)
            {
                await _pendingApply;
            }

            // If settings changed while discovery ran, the startup reconcile below
            // would target a stale server/workshop; leave the newer state alone.
            if (SettingsChanged(startupSettings, _settings))
            {
                return;
            }

            MapTypes.Refresh(_settings, ModState.WorkshopMods.ToList(), ModState.LoadedMods.ToList());
            MapTypes.ReconcileAppliedMap();
            Mods.MarkApplied();
            UpdateDirty();
        }
        catch (Exception ex)
        {
            Log.Error($"Initialization failed: {ex.Message}");
        }
    }

    private static bool SettingsChanged(Settings before, Settings after) =>
        !string.Equals(before.WorkshopPath, after.WorkshopPath, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(before.ServerPath, after.ServerPath, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(before.BatFileName, after.BatFileName, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(before.DataDirectory, after.DataDirectory, StringComparison.OrdinalIgnoreCase);

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
        string dataDirectory = _dataDirectoryProvider.Current;
        ConfigLoadResult<Settings> result = _settingsService.Load(dataDirectory);

        if (result.Status == ConfigLoadStatus.Success)
        {
            return result.Value!;
        }

        if (result.Status == ConfigLoadStatus.Corrupt)
        {
            bool backedUp = _settingsService.BackupCorrupt(dataDirectory);
            string message = backedUp
                ? "settings.json was corrupt and could not be read. Its contents were preserved as \"settings.json.corrupt\" and new defaults were created."
                : "settings.json was corrupt and could not be read, and a backup could not be created. New defaults were created.";
            Log.Error(message);
            _dialogs.ShowMessage(message, "Settings file corrupt", isError: true);

            var defaults = new Settings();
            _settingsService.Save(dataDirectory, defaults);
            return defaults;
        }

        Log.Warning("No configuration found. Set the server and workshop paths in the Settings tab.");
        var fresh = new Settings();
        _settingsService.Save(dataDirectory, fresh);
        return fresh;
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

    /// <summary>Applies pending changes if any. Used when leaving the Mods tab or closing the window.</summary>
    public async Task ApplyIfDirtyAsync()
    {
        if (IsDirty)
        {
            await ApplyAsync();
        }
    }

    /// <summary>
    /// Runs a single Apply at a time: overlapping requests (e.g. tab switch plus
    /// Start Server) share the in-flight apply instead of writing files twice.
    /// </summary>
    private Task<bool> ApplyAsync() => _pendingApply ??= ApplyCoreAsync();

    private async Task<bool> ApplyCoreAsync()
    {
        try
        {
            Settings newSettings = Settings.ToSettings();
            IReadOnlyList<string> loadedMods = ModState.LoadedMods.ToList();

            string dataDirectory = _dataDirectoryProvider.Resolve(newSettings);

            ApplyResult result = await Task.Run(() => _applyService.Apply(new ApplyContext
            {
                Settings = newSettings,
                LoadedMods = loadedMods,
                DataDirectory = dataDirectory,
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

            // Relocate the data files only after a successful apply so a failed
            // Apply never moves them.
            if (!string.Equals(dataDirectory, _dataDirectoryProvider.Current, StringComparison.OrdinalIgnoreCase))
            {
                _dataDirectoryProvider.MoveTo(dataDirectory, newSettings);
            }

            // From here the apply is committed. A UI refresh failure is logged as
            // a warning rather than flipping a successful apply to a failure.
            try
            {
                bool pathChanged = newSettings.WorkshopPath != _settings.WorkshopPath
                    || newSettings.ServerPath != _settings.ServerPath;

                _settings = newSettings;
                Settings.MarkApplied(newSettings);
                Mods.MarkApplied(loadedMods);

                if (pathChanged)
                {
                    await Mods.RefreshAsync(newSettings.WorkshopPath);
                    MapTypes.Refresh(newSettings, ModState.WorkshopMods.ToList(), ModState.LoadedMods.ToList());
                    MapTypes.ReconcileAppliedMap();
                }
                else
                {
                    MapTypes.Sync(ModState.WorkshopMods, ModState.LoadedMods);
                }

                MapTypes.SyncEconomyCore();
            }
            catch (Exception ex)
            {
                Log.Error($"Apply succeeded, but refreshing the UI failed: {ex.Message}");
            }

            UpdateDirty();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Apply failed: {ex.Message}");
            return false;
        }
        finally
        {
            _pendingApply = null;
        }
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

    /// <summary>
    /// Relocates the data files immediately when the user browses a new data
    /// directory, persisting the override into settings.json. Runs only after any
    /// in-flight Apply has finished so the two never relocate concurrently.
    /// </summary>
    private async void HandleDataDirectoryChanged()
    {
        if (_relocatingDataDirectory)
        {
            return;
        }

        _relocatingDataDirectory = true;
        try
        {
            // Wait for an Apply that may still be writing into the old directory.
            if (_pendingApply is not null)
            {
                await _pendingApply;
            }

            string picked = Settings.DataDirectory;
            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            // Persist the override on top of the applied baseline (not the raw
            // form, which may hold un-applied path edits) so a restart never
            // presents never-applied settings as the applied state.
            var persisted = _settings with { DataDirectory = picked };
            _dataDirectoryProvider.MoveTo(picked, persisted);
            _settings = persisted;
            Settings.NotifyDataDirectoryApplied();
        }
        finally
        {
            _relocatingDataDirectory = false;
        }
    }
}
