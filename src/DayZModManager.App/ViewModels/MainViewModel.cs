using System.IO;
using DayZModManager.Core;
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
    private readonly IServerLogCleanupService _logCleanup;
    private readonly IDialogService _dialogs;
    private readonly IDayZServerProcessState _serverProcess;
    private readonly IPresetService _presetService;
    private readonly IJunctionService _junctions;
    private readonly IValidationService _validation;
    private readonly ILaunchEnvironmentService _launchEnvironment;

    private Settings _settings = new();
    private bool _isDirty;
	private bool _startupFellBackToDefaultPreset;
    private string _statusText = "Configuration applied";
    private Task<bool>? _pendingApply;

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
        ILaunchEnvironmentService launchEnvironment,
        IFileSystem fileSystem,
        IDialogService dialogs,
        IProcessLauncher launcher,
        IServerLogCleanupService logCleanup,
        IDayZServerProcessState serverProcessState,
        IDataDirectoryProvider dataDirectoryProvider,
        IPresetService presetService,
        IJunctionService junctions,
        ITypeFileJunctionService typeFileJunctions,
        IValidationService validation)
    {
        _settingsService = settingsService;
        _modOrderStore = modOrderStore;
        _typesConfigStore = typesConfigStore;
        _applyService = applyService;
        _launcher = launcher;
        _logCleanup = logCleanup;
        _dataDirectoryProvider = dataDirectoryProvider;
        _dialogs = dialogs;
        _serverProcess = serverProcessState;
        _presetService = presetService;
        _junctions = junctions;
        _validation = validation;
        _launchEnvironment = launchEnvironment;

        Log = new LogViewModel();
        ModState = new ModState();
        TypesConfig = new TypesConfig();

        // --- Load persistent state ---
        _settings = LoadOrCreateSettings();

        // Correct a workshop path that points at the DayZ game folder instead of
        // its "!Workshop" subfolder, and persist the correction so discovery,
        // junctions and validation all use the right location.
        string resolvedWorkshop = WorkshopPathResolver.Resolve(fileSystem, _settings.WorkshopPath);
        if (!string.Equals(resolvedWorkshop, _settings.WorkshopPath, StringComparison.OrdinalIgnoreCase))
        {
            _settings = _settings with { WorkshopPath = resolvedWorkshop };
            try
            {
                _settingsService.Save(_dataDirectoryProvider.Current, _settings);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to persist the corrected workshop path: {ex.Message}");
            }
        }

        // Preset-scoped configuration: mod order and types live in the active
        // preset's folder. Until a map is applied there is no active preset.
        string activePreset = ResolveActivePreset(_settings, _settings.ActiveMap);
		
		// A persisted active preset may point at a folder that is missing or
		// incomplete (for example, a folder created by hand, or one whose config was
		// removed). Fall back to the map's default preset so the session loads a
		// usable environment; the map switch then persists the corrected selection.
		if (!string.IsNullOrWhiteSpace(_settings.ActiveMap)
			&& !_presetService.PresetExists(_dataDirectoryProvider.Current, _settings.ActiveMap, activePreset))
		{
			string? problem = _presetService.DescribeIncompletePreset(
				_dataDirectoryProvider.Current, _settings.ActiveMap, activePreset);
		
			if (PresetPaths.IsDefaultPreset(activePreset))
			{
				// The default preset cannot fall back any further; surface the exact
				// problem so the user can restore it themselves.
				Log.Warning(
					$"The default preset for {_settings.ActiveMap} is incomplete: {problem}. " +
					"restore it by deleting the __default_preset__ folder, and the program will generate a new one.");
			}
			else
			{
				Log.Warning($"Previously active preset \"{activePreset}\" for {_settings.ActiveMap} was not found or is incomplete. Switching to the default preset.");
				activePreset = PresetPaths.DefaultPresetName;
				_startupFellBackToDefaultPreset = true;
			}
		}


        string? presetFolder = string.IsNullOrWhiteSpace(_settings.ActiveMap)
            ? null
            : PresetPaths.PresetFolder(_dataDirectoryProvider.Current, _settings.ActiveMap, activePreset);

        if (presetFolder is not null)
        {
            ConfigLoadResult<IReadOnlyList<string>> order = _modOrderStore.Load(presetFolder);
            if (order.Status == ConfigLoadStatus.Success)
            {
                ModState.ReplaceLoadedMods(order.Value!);
            }
            else if (order.Status == ConfigLoadStatus.Corrupt)
            {
                bool backedUp = _modOrderStore.BackupCorrupt(presetFolder);
                Log.Error(
                    "mod_order.json is corrupt and was ignored." +
                    (backedUp ? " A backup was saved as mod_order.json.corrupt." : string.Empty));
            }

            ConfigLoadResult<TypesConfig> types = _typesConfigStore.Load(presetFolder);
            if (types.Status == ConfigLoadStatus.Success)
            {
                ApplyLoadedTypes(types.Value!);
            }
            else if (types.Status == ConfigLoadStatus.Corrupt)
            {
                bool backedUp = _typesConfigStore.BackupCorrupt(presetFolder);
                TypesConfig.IsCorrupt = true;
                Log.Error(
                    "types_config.json is corrupt and was ignored. The file was preserved." +
                    (backedUp ? " A backup was saved as types_config.json.corrupt." : string.Empty) +
                    " Fix or delete it before editing types.");
            }
        }

        // CurrentMap is runtime-only now (the file is map-agnostic); restore it
        // from the persisted active map before the types page is built.
        TypesConfig.CurrentMap = _settings.ActiveMap;

        // --- Build child view models ---
        Mods = new ModsViewModel(ModState, discoveryService, Log);
        PresetTypes = new PresetTypesViewModel(
            mapService, typesService, saveGameService, typesConfigStore, serverConfigService,
            fileSystem, dialogs, Log, TypesConfig, _dataDirectoryProvider, serverProcessState, _launcher,
            presetService, _junctions, typeFileJunctions, launchEnvironment)
        {
            ActivePresetName = activePreset,
        };
        PresetTypes.EnsureApplied = EnsureApplied;
        PresetTypes.ActivatePreset = ActivatePresetAsync;
        PresetTypes.ResolvePresetForMap = map => ResolveActivePreset(_settings, map);
        PresetTypes.PersistActiveSelection = PersistActiveSelection;
        Settings = new SettingsViewModel(dialogs, fileSystem);
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

            IReadOnlyList<string> instanceIdIssues = _presetService.FindInstanceIdIssues(_dataDirectoryProvider.Current);
            if (instanceIdIssues.Count > 0)
            {
                foreach (string issue in instanceIdIssues)
                {
                    Log.Error(issue);
                }

                _dialogs.ShowWarning(
                    "Preset instance IDs are not unique or do not match their serverDZ.cfg.",
                    "Preset instance ID problem",
                    "Worlds or DZMMV2_ModList junctions may be shared. Fix the affected presets before starting the server.",
                    string.Join(Environment.NewLine, instanceIdIssues));
            }

            PresetTypes.Refresh(_settings, ModState.WorkshopMods.ToList(), ModState.LoadedMods.ToList());
            string? autoAppliedMap = PresetTypes.ReconcileAppliedMap();
            Mods.MarkApplied();
            UpdateDirty();
			
            if (_startupFellBackToDefaultPreset)
            {
                await ApplyStartupFallbackAsync();
            }
            else if (autoAppliedMap is not null && Settings.CanApply)
            {
                // The first map was just auto-applied. Run the normal activation so
                // its mod order is loaded and its junctions/mod list are provisioned
                // before the first launch, instead of leaving a batch file whose mods
                // have no matching junctions.
                await ActivatePresetAsync(autoAppliedMap, ResolveActivePreset(_settings, autoAppliedMap));
            }

            await CleanupOldLogsIfEnabledAsync(_settings);
        }
        catch (Exception ex)
        {
            Log.Error($"Initialization failed: {ex.Message}");
        }
    }

    private static bool SettingsChanged(Settings before, Settings after) =>
        !string.Equals(before.WorkshopPath, after.WorkshopPath, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(before.ServerPath, after.ServerPath, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(before.BatFileName, after.BatFileName, StringComparison.OrdinalIgnoreCase);

    public LogViewModel Log { get; }

    public ModState ModState { get; }

    public TypesConfig TypesConfig { get; }

    public ModsViewModel Mods { get; }

    public PresetTypesViewModel PresetTypes { get; }

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

    /// <summary>Refreshes the Preset &amp; Types mod dropdown from the current in-memory loaded mods.</summary>
    public void OnPresetTypesTabActivated()
    {
        PresetTypes.Sync(ModState.WorkshopMods, ModState.LoadedMods);
    }

    /// <summary>Resolves the preset active for a map, defaulting to the reserved default preset.</summary>
    private static string ResolveActivePreset(Settings settings, string mapName)
    {
        if (!string.IsNullOrWhiteSpace(mapName)
            && settings.ActivePresets.TryGetValue(mapName, out string? preset)
            && !string.IsNullOrWhiteSpace(preset))
        {
            return preset;
        }

        return PresetPaths.DefaultPresetName;
    }

    /// <summary>Returns a copy of <paramref name="settings"/> recording the active preset for a map.</summary>
    private static Settings WithActiveSelection(Settings settings, string mapName, string presetName)
    {
        var active = new Dictionary<string, string>(settings.ActivePresets, StringComparer.OrdinalIgnoreCase)
        {
            [mapName] = presetName,
        };
        return settings with { ActiveMap = mapName, ActivePresets = active };
    }

    /// <summary>
    /// Records the map/preset that a map apply just made active and persists it, so
    /// the next startup can locate the preset's mod order and types configuration.
    /// Invoked by the Preset &amp; Types page after a successful map apply.
    /// </summary>
    private void PersistActiveSelection(string mapName, string presetName)
    {
        if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(presetName))
        {
            return;
        }

        _settings = WithActiveSelection(_settings, mapName, presetName);
        try
        {
            _settingsService.Save(_dataDirectoryProvider.Current, _settings);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to persist the active map/preset selection: {ex.Message}");
        }
    }

    /// <summary>
    /// Makes <paramref name="presetName"/> the active preset for a map: loads its
    /// mod order and types configuration, persists the selection, and applies the
    /// preset's environment. Invoked by the Preset &amp; Types page.
    /// </summary>
    private async Task<bool> ActivatePresetAsync(string mapName, string presetName)
    {
        if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(presetName))
        {
            return false;
        }

        string dataDirectory = _dataDirectoryProvider.Current;
        string presetFolder = PresetPaths.PresetFolder(dataDirectory, mapName, presetName);

        if (!PresetPaths.IsDefaultPreset(presetName)
            && !_presetService.PresetExists(dataDirectory, mapName, presetName))
        {
            Log.Error($"Preset \"{presetName}\" does not exist or is incomplete for {mapName}.");
            return false;
        }

        // Let any in-flight apply finish so this activation's own apply is not
        // silently merged with it (ApplyAsync de-duplicates concurrent calls, which
        // would otherwise leave this preset's mod order unwritten).
        if (_pendingApply is not null)
        {
            await _pendingApply;
        }

        // Snapshot the current selection and preset-scoped state so a failed apply
        // can restore them. Activating a preset mutates the active selection before
        // the apply (the apply reads it to know what to write), so without a rollback
        // a failure would leave the tool pointing at a preset it never applied.
        string previousPreset = PresetTypes.ActivePresetName;
        Settings previousSettings = _settings;
        IReadOnlyList<string> previousMods = ModState.LoadedMods.ToList();
        List<ModTypesEntry> previousTypes = TypesConfig.Mods.ToList();
        string previousCurrentMap = TypesConfig.CurrentMap;
        bool previousCorrupt = TypesConfig.IsCorrupt;

        ConfigLoadResult<IReadOnlyList<string>> order = _modOrderStore.Load(presetFolder);
        if (order.Status == ConfigLoadStatus.Corrupt)
        {
            bool backedUp = _modOrderStore.BackupCorrupt(presetFolder);
            Log.Error(
                "mod_order.json is corrupt for the selected preset; it was ignored." +
                (backedUp ? " A backup was saved as mod_order.json.corrupt." : string.Empty));
        }

        IReadOnlyList<string> mods = order.Status == ConfigLoadStatus.Success
            ? order.Value!
            : Array.Empty<string>();
        Mods.ReplaceLoadedMods(mods);

        ConfigLoadResult<TypesConfig> types = _typesConfigStore.Load(presetFolder);
        if (types.Status == ConfigLoadStatus.Success)
        {
            ApplyLoadedTypes(types.Value!);
        }
        else
        {
            TypesConfig.Mods.Clear();
            TypesConfig.IsCorrupt = types.Status == ConfigLoadStatus.Corrupt;
            if (TypesConfig.IsCorrupt)
            {
                bool backedUp = _typesConfigStore.BackupCorrupt(presetFolder);
                Log.Error(
                    "types_config.json is corrupt for the selected preset; it was ignored and will not be overwritten." +
                    (backedUp ? " A backup was saved as types_config.json.corrupt." : string.Empty));
            }
        }

        // The environment being applied targets this map. Set it before the apply so
        // the launch batch and mod order are written for the right map even on a
        // cross-map switch; the snapshot above captured the previous map, so a failed
        // apply restores it.
        TypesConfig.CurrentMap = mapName;

        _settings = WithActiveSelection(_settings, mapName, presetName);
        PresetTypes.ActivePresetName = presetName;

        bool applied = await ApplyAsync();
        if (!applied)
        {
            _settings = previousSettings;
            Mods.ReplaceLoadedMods(previousMods);
            TypesConfig.Mods = previousTypes;
            TypesConfig.CurrentMap = previousCurrentMap;
            TypesConfig.IsCorrupt = previousCorrupt;
            PresetTypes.ActivePresetName = previousPreset;
            Log.Error($"Failed to apply preset \"{presetName}\"; restored the previous preset.");
        }

        return applied;
    }
	
	/// <summary>
	/// Repairs a startup fallback. When the previously active preset was missing or
	/// incomplete, the in-memory selection was moved to the default preset, but the
	/// launch batch and cfgeconomycore still referenced the old preset, so Start
	/// Server would launch an environment whose config no longer exists. Switch to
	/// the default preset through the normal activation path (the same path a manual
	/// preset switch uses) so the batch, economy and persisted selection are all
	/// corrected before the server can be launched.
	/// </summary>
	private async Task ApplyStartupFallbackAsync()
	{
		string mapName = _settings.ActiveMap;
		if (string.IsNullOrWhiteSpace(mapName))
		{
			return;
		}
	
		// Without a server path and launch batch file an Apply cannot run; the
		// in-memory fallback remains, so tell the user how to complete it.
		if (!Settings.CanApply)
		{
			Log.Warning(
				"The default preset was selected because the previous preset is unavailable, " +
				"but it could not be applied: set the server path and launch batch file, then select a preset.");
			return;
		}
	
		if (await ActivatePresetAsync(mapName, PresetPaths.DefaultPresetName))
		{
			PersistActiveSelection(mapName, PresetPaths.DefaultPresetName);
			Log.Success($"Default preset applied for {mapName}.");
		}
	}

    private Settings LoadOrCreateSettings()
    {
        string dataDirectory = _dataDirectoryProvider.Current;
        ConfigLoadResult<Settings> result = _settingsService.Load(dataDirectory);

        if (result.Status == ConfigLoadStatus.Success)
        {
            Settings loaded = result.Value!;
            // A leftover settings.json from an earlier install may hold only empty
            // defaults; treat it the same as a missing file so the user is
            // reminded to configure the paths.
            if (HasNoConfiguredPaths(loaded))
            {
                Log.Warning("No configuration found. Set the server and workshop paths in the Settings tab.");
            }

            return loaded;
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

    /// <summary>True when the settings hold no usable paths yet (nothing configured).</summary>
    private static bool HasNoConfiguredPaths(Settings settings) =>
        string.IsNullOrWhiteSpace(settings.WorkshopPath) && string.IsNullOrWhiteSpace(settings.ServerPath);

    private void ApplyLoadedTypes(TypesConfig loaded)
    {
        TypesConfig.Mods = loaded.Mods.ToList();
        TypesConfig.IsCorrupt = false;
    }

    /// <summary>Applies pending changes if any. Used when leaving the Mods tab or closing the window.</summary>
    public async Task ApplyIfDirtyAsync()
    {
        // Skip while the configuration is incomplete (no workshop/server/batch
        // file yet): an Apply could not pass validation and would only log errors.
        if (IsDirty && Settings.CanApply)
        {
            await ApplyAsync();
        }
    }

    /// <summary>
    /// Runs a single Apply at a time: overlapping requests (e.g. tab switch plus
    /// Start Server) share the in-flight apply instead of writing files twice.
    /// </summary>
    private Task<bool> ApplyAsync() => _pendingApply ?? StartApplyAsync();

    /// <summary>
    /// Starts the in-flight Apply and clears it only after the core apply has
    /// completed. Assigning <see cref="_pendingApply"/> after calling
    /// <see cref="ApplyCoreAsync"/> would otherwise leave a synchronously completed
    /// apply (for example, a pre-flight validation failure) cached forever.
    /// </summary>
    private async Task<bool> StartApplyAsync()
    {
        Task<bool> apply = ApplyCoreAsync();
        _pendingApply = apply;
        try
        {
            return await apply;
        }
        finally
        {
            if (ReferenceEquals(_pendingApply, apply))
            {
                _pendingApply = null;
            }
        }
    }

    private async Task<bool> ApplyCoreAsync()
    {
        try
        {
            Settings newSettings = Settings.ToSettings() with
            {
                // The active map/preset selection is app state the Settings page does
                // not edit, so it must be carried through a round-trip.
                ActiveMap = _settings.ActiveMap,
                ActivePresets = _settings.ActivePresets,
            };
            IReadOnlyList<string> loadedMods = ModState.LoadedMods.ToList();

            bool pathsChanged = newSettings.WorkshopPath != _settings.WorkshopPath
                || newSettings.ServerPath != _settings.ServerPath
                || newSettings.BatFileName != _settings.BatFileName;
            bool modsChanged = Mods.IsDirty;

            // A change limited to Settings feature toggles (e.g. the log-cleanup
            // checkbox) needs only the setting persisted - not the full server
            // configuration apply with its junction/batch work and log block.
            if (!pathsChanged && !modsChanged && Settings.IsDirty)
            {
                return await ApplyFeatureOnlyAsync(newSettings);
            }

            string dataDirectory = _dataDirectoryProvider.Resolve(newSettings);

            // Resolve the active map/preset's launch environment in one place. The
            // preset lives in the current data directory (which may differ from the
            // relocation target), while the profile/config paths written to the batch
            // file are anchored at the resolved target.
            string mapName = TypesConfig.CurrentMap;
            string activePreset = PresetTypes.ActivePresetName;
            EnvironmentResolution resolution = _presetService.ResolveEnvironment(
                newSettings, dataDirectory, mapName, activePreset,
                presetLookupDirectory: _dataDirectoryProvider.Current);
            if (resolution.Error is not null)
            {
                Log.Error(resolution.Error);
                return false;
            }

            foreach (string warning in resolution.Warnings)
            {
                Log.Warning(warning);
            }

            ApplyResult result = await Task.Run(() => _applyService.Apply(new ApplyContext
            {
                Settings = newSettings,
                LoadedMods = loadedMods,
                DataDirectory = dataDirectory,
                Environment = resolution.Environment,
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

                // Migrating the Presets tree can carry a stale mod_order.json over
                // the file the Apply just wrote. Re-save the authoritative order so
                // the relocated preset matches the applied configuration.
                if (!string.IsNullOrWhiteSpace(mapName))
                {
                    try
                    {
                        _modOrderStore.Save(PresetPaths.PresetFolder(dataDirectory, mapName, activePreset), loadedMods);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Failed to re-save the mod order after relocating the data directory: {ex.Message}");
                    }
                }
            }

            // From here the apply is committed. A UI refresh failure is logged as
            // a warning rather than flipping a successful apply to a failure.
            try
            {
                bool workshopOrServerChanged = newSettings.WorkshopPath != _settings.WorkshopPath
                    || newSettings.ServerPath != _settings.ServerPath;
                bool batchChanged = newSettings.BatFileName != _settings.BatFileName;
                string? autoAppliedMap = null;

                bool cleanupWasEnabled = _settings.AutoCleanServerLogs;

                _settings = newSettings;
                Settings.MarkApplied(newSettings);
                Mods.MarkApplied(loadedMods);
                LogCleanupStateChange(cleanupWasEnabled, newSettings.AutoCleanServerLogs);

                if (workshopOrServerChanged)
                {
                    await Mods.RefreshAsync(newSettings.WorkshopPath);
                }

                // Map & Types must be refreshed when the launch batch changes too:
                // it caches the batch file name and would otherwise keep writing
                // serverProfile into the previously selected batch.
                if (workshopOrServerChanged || batchChanged)
                {
                    PresetTypes.Refresh(newSettings, ModState.WorkshopMods.ToList(), ModState.LoadedMods.ToList());
                    autoAppliedMap = PresetTypes.ReconcileAppliedMap();
                }
                else
                {
                    PresetTypes.Sync(ModState.WorkshopMods, ModState.LoadedMods);
                }

                if (!PresetTypes.SyncEconomyCore())
                {
                    Log.Error(
                        "Apply completed, but cfgeconomycore.xml could not be synchronized. " +
                        "Start Server is blocked until the configuration is re-applied successfully.");
                }

                // The first Apply after configuring the server has no active map yet,
                // so it wrote an empty mod list and could not save mod_order.json.
                // Reconcile just auto-applied the first map; run the apply again now
                // that the map/preset environment is active, so the loaded mods are
                // written to its mod order, batch file and junctions. The second pass
                // cannot auto-apply again because CurrentMap is now set.
                if (autoAppliedMap is not null)
                {
                    return await ApplyCoreAsync();
                }
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
    }

    /// <summary>
    /// Persists a Settings change that is limited to feature toggles (the paths
    /// and the mod list are unchanged), so no junction/batch/server configuration
    /// work or its log block is needed. The data directory never changes here.
    /// </summary>
    private async Task<bool> ApplyFeatureOnlyAsync(Settings newSettings)
    {
        bool cleanupWasEnabled = _settings.AutoCleanServerLogs;
        string dataDirectory = _dataDirectoryProvider.Resolve(newSettings);

        // Persist before committing so a failure keeps the page dirty.
        await Task.Run(() => _settingsService.Save(dataDirectory, newSettings));

        _settings = newSettings;
        Settings.MarkApplied(newSettings);
        LogCleanupStateChange(cleanupWasEnabled, newSettings.AutoCleanServerLogs);

        UpdateDirty();
        return true;
    }

    /// <summary>Logs the log-cleanup feature once per enable/disable transition.</summary>
    private void LogCleanupStateChange(bool wasEnabled, bool nowEnabled)
    {
        if (nowEnabled && !wasEnabled)
        {
            Log.Info("Log cleanup is enabled.");
        }
        else if (!nowEnabled && wasEnabled)
        {
            Log.Info("Log cleanup is disabled.");
        }
    }

    private async Task StartServerAsync()
    {
        if (RefuseLaunchWhileServerRunning())
        {
            return;
        }

        // Never launch while a map switch or types operation is in flight: it may
        // be rewriting the launch batch file / mission files under us. Wait for it
        // to settle (bounded) before proceeding.
        if (!await WaitUntilPresetTypesIdleAsync())
        {
            ShowLaunchBlocked("A map or types operation is still in progress.");
            return;
        }

        if (IsDirty && !await ApplyAsync())
        {
            return;
        }

        // An Apply can itself trigger a first-time map switch (ReconcileAppliedMap);
        // let any in-flight operation finish writing the batch file before launch.
        if (!await WaitUntilPresetTypesIdleAsync())
        {
            ShowLaunchBlocked("A map or types operation is still in progress.");
            return;
        }

        if (RefuseLaunchWhileServerRunning())
        {
            return;
        }

        // Mandatory preflight: validate the environment even when nothing is
        // pending, so a stale-yet-clean state (a mod removed from the Workshop,
        // a referenced type file deleted, an incomplete preset) cannot launch.
        if (!ValidateBeforeLaunch())
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

    /// <summary>
    /// Runs the launch preflight: the standard configuration validation (paths,
    /// batch and server config), the loaded mods missing from the Workshop (Apply
    /// tolerates these, so they are checked here only), and the active preset's
    /// types issues (config entries for Workshop-missing mods, and referenced type
    /// files missing from type_files). Returns false and reports the problems when
    /// the environment is not ready to launch.
    /// </summary>
    private bool ValidateBeforeLaunch()
    {
        var errors = new List<string>(_validation.Validate(new ValidationContext
        {
            Settings = _settings,
            LoadedMods = ModState.LoadedMods,
            ActiveServerConfigPath = ActivePresetServerConfigPath(),
        }));

        foreach (string mod in _validation.FindMissingMods(_settings.WorkshopPath, ModState.LoadedMods))
        {
            errors.Add($"Mod not found in workshop: {mod}");
        }

        errors.AddRange(PresetTypes.PreflightTypesIssues());
        errors.AddRange(_presetService.FindInstanceIdConflicts(_dataDirectoryProvider.Current));

        if (TypesConfig.IsCorrupt)
        {
            errors.Add("types_config.json is corrupt for the active preset. Fix or delete it before starting the server.");
        }

        if (errors.Count == 0)
        {
            return true;
        }

        ShowLaunchInvalid(string.Join(" ", errors));
        return false;
    }

    /// <summary>
    /// The active preset's <c>serverDZ.cfg</c> path, or null when no map is applied
    /// (the server-root config is validated instead).
    /// </summary>
    private string? ActivePresetServerConfigPath()
    {
        string mapName = TypesConfig.CurrentMap;
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return null;
        }

        return PresetPaths.ServerConfigPath(
            _dataDirectoryProvider.Current, mapName, PresetTypes.ActivePresetName);
    }

    /// <summary>Reports a launch blocked by an invalid environment (log + modal).</summary>
    private void ShowLaunchInvalid(string reason)
    {
        Log.Error(reason);
        _dialogs.ShowMessage(reason, "Start Server", isError: true);
    }

    /// <summary>
    /// Returns true (and tells the user) when the DayZ server is already running,
    /// preventing a second instance from being launched against the same profile.
    /// </summary>
    private bool RefuseLaunchWhileServerRunning()
    {
        if (!_serverProcess.IsDayZServerRunning())
        {
            return false;
        }

        string message = "The DayZ server appears to be already running. Stop it before starting another instance.";
        Log.Error(message);
        _dialogs.ShowMessage(message, "DayZ Server running", isError: true);
        return true;
    }

    /// <summary>
    /// Waits (bounded) until no map-switch / types operation is in flight so the
    /// launch batch and mission files are stable before the server is started.
    /// Returns false when the wait timed out.
    /// </summary>
    private async Task<bool> WaitUntilPresetTypesIdleAsync(int timeoutMs = 30000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (PresetTypes.IsBusy && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        return !PresetTypes.IsBusy;
    }

    private void ShowLaunchBlocked(string reason)
    {
        Log.Error($"{reason} Server launch was cancelled; try again once it completes.");
        _dialogs.ShowMessage($"{reason} Try starting the server again once it completes.", "Start Server", isError: true);
    }

    /// <summary>
    /// Clears the active preset's server log files when the "auto-clean" feature is
    /// enabled. Runs once at app startup: when more than
    /// <see cref="ServerLogCleanupService.CleanupThreshold"/> <c>.rpt</c>/<c>.log</c>
    /// files have accumulated in its profiles folder, all of them are deleted.
    /// </summary>
    private async Task CleanupOldLogsIfEnabledAsync(Settings settings)
    {
        if (!settings.AutoCleanServerLogs)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.ServerPath))
        {
            Log.Info("Log cleanup skipped: no server path is configured.");
            return;
        }

        string? mapName = TypesConfig.CurrentMap;
        if (string.IsNullOrWhiteSpace(mapName))
        {
            Log.Info("Log cleanup skipped: no active map is configured yet.");
            return;
        }

        // The active preset's profiles folder is where DayZ writes the .RPT and
        // .log files.
        string activePreset = PresetTypes.ActivePresetName;
        string folder = PresetPaths.ProfilesFolder(_dataDirectoryProvider.Current, mapName, activePreset);

        ServerLogCleanupResult result = await Task.Run(() => _logCleanup.Cleanup(folder));
        if (!result.FolderExists)
        {
            Log.Info($"Log cleanup skipped: profile folder not found: {folder}");
            return;
        }

        // Stay silent when nothing needed deleting so opening the app does not
        // spam the log on every normal run.
        if (result.Removed == 0)
        {
            return;
        }

        Log.Info($"Log cleanup: removed {result.Removed} log file(s).");
    }

    private void UpdateDirty()
    {
        bool dirty = Mods.IsDirty || Settings.IsDirty;
        IsDirty = dirty;
        StatusText = dirty ? "\u25CF Unsaved changes" : "\u2713 Configuration applied";
    }

    private Task<bool> EnsureApplied()
    {
        if (!IsDirty)
        {
            return Task.FromResult(true);
        }

        // Refuse to run an Apply that cannot pass while the configuration is
        // incomplete, so map/types operations do not proceed unconfigured.
        if (!Settings.CanApply)
        {
            return Task.FromResult(false);
        }

        return ApplyAsync();
    }
}
