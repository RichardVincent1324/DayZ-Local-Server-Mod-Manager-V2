using System.Collections.ObjectModel;
using System.IO;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.App.Services;

namespace DayZModManager.App.ViewModels;

/// <summary>
/// Backs the "Map & Types" page. Map selection and types configuration are
/// applied immediately, not deferred to the main Apply.
/// </summary>
public sealed class MapTypesViewModel : ViewModelBase
{
    private readonly IMapService _mapService;
    private readonly ITypesService _typesService;
    private readonly ITypesConfigStore _typesConfigStore;
    private readonly IServerConfigService _serverConfig;
    private readonly IBatchFileService _batchFile;
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogs;
    private readonly LogViewModel _log;
    private readonly TypesConfig _typesConfig;
    private readonly string _dataDirectory;

    private string _serverPath = string.Empty;
    private string _workshopPath = string.Empty;
    private string _batFileName = string.Empty;
    private List<string> _allMods = new();
    private IReadOnlyList<string> _loadedMods = Array.Empty<string>();
    private IReadOnlyList<MapInfo> _discoveredMaps = Array.Empty<MapInfo>();

    private string? _selectedMap;
    private string? _selectedMod;

    public MapTypesViewModel(
        IMapService mapService,
        ITypesService typesService,
        ITypesConfigStore typesConfigStore,
        IServerConfigService serverConfig,
        IBatchFileService batchFile,
        IFileSystem fileSystem,
        IDialogService dialogs,
        LogViewModel log,
        TypesConfig typesConfig,
        string dataDirectory)
    {
        _mapService = mapService;
        _typesService = typesService;
        _typesConfigStore = typesConfigStore;
        _serverConfig = serverConfig;
        _batchFile = batchFile;
        _fileSystem = fileSystem;
        _dialogs = dialogs;
        _log = log;
        _typesConfig = typesConfig;
        _dataDirectory = dataDirectory;

        ApplyMapCommand = new RelayCommand(ApplyMap);
        ConfigXmlCommand = new RelayCommand(ConfigureMod);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => CanRemoveSelected);
        CleanInvalidCommand = new RelayCommand(CleanInvalid);

        SelectedTypesRows.CollectionChanged += (_, _) => NotifyCommandStates();
    }

    public ObservableCollection<string> MapNames { get; } = new();

    public ObservableCollection<string> ModNames { get; } = new();

    public ObservableCollection<TypesRowViewModel> TypesRows { get; } = new();

    public ObservableCollection<TypesRowViewModel> SelectedTypesRows { get; } = new();

    public string? SelectedMap
    {
        get => _selectedMap;
        set
        {
            if (SetField(ref _selectedMap, value))
            {
                OnPropertyChanged(nameof(CanApplyMap));
            }
        }
    }

    public string? SelectedMod
    {
        get => _selectedMod;
        set => SetField(ref _selectedMod, value);
    }

    public bool CanApplyMap => SelectedMap is not null && SelectedMap != _typesConfig.CurrentMap;

    private bool CanRemoveSelected => SelectedTypesRows.Count > 0;

    /// <summary>
    /// Invoked before configuring types. Returns true when the current in-memory
    /// state is persisted (either already or just applied), false if Apply failed.
    /// </summary>
    public Func<Task<bool>>? EnsureApplied { get; set; }

    public RelayCommand ApplyMapCommand { get; }
    public RelayCommand ConfigXmlCommand { get; }
    public RelayCommand RemoveSelectedCommand { get; }
    public RelayCommand CleanInvalidCommand { get; }

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

        ApplyDefaultMapIfNeeded();
    }

    /// <summary>
    /// Applies the first discovered map when no map has been applied yet, so a
    /// first-time user starts with a valid (already applied) default map.
    /// </summary>
    public void ApplyDefaultMapIfNeeded()
    {
        if (!string.IsNullOrEmpty(_typesConfig.CurrentMap) || MapNames.Count == 0)
        {
            return;
        }

        SelectedMap = MapNames[0];
        ApplyMapCore(MapNames[0]);
    }

    /// <summary>Rebuilds the map dropdown from discovered maps plus any maps already configured.</summary>
    private void RebuildMapNames()
    {
        string? previousMap = SelectedMap;

        _discoveredMaps = _mapService.DiscoverMaps(_serverPath);

        MapNames.Clear();
        foreach (MapInfo map in _discoveredMaps)
        {
            MapNames.Add(map.Name);
        }

        foreach (string key in _typesConfig.Maps.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!MapNames.Contains(key))
            {
                MapNames.Add(key);
            }
        }

        if (!string.IsNullOrEmpty(_typesConfig.CurrentMap) && MapNames.Contains(_typesConfig.CurrentMap))
        {
            SelectedMap = _typesConfig.CurrentMap;
        }
        else if (previousMap is not null && MapNames.Contains(previousMap))
        {
            SelectedMap = previousMap;
        }
        else
        {
            SelectedMap = MapNames.Count > 0 ? MapNames[0] : null;
        }
    }

    /// <summary>Rebuilds the types-config mod dropdown from the currently loaded mods.</summary>
    public void RefreshModNames(IReadOnlyList<string> loadedMods)
    {
        string? previous = SelectedMod;

        ModNames.Clear();
        foreach (string mod in loadedMods)
        {
            ModNames.Add(mod);
        }

        if (previous is not null && ModNames.Contains(previous))
        {
            SelectedMod = previous;
        }
        else
        {
            SelectedMod = ModNames.Count > 0 ? ModNames[0] : null;
        }
    }

    /// <summary>Regenerates cfgeconomycore.xml for the applied map, referencing only loaded mods' types.</summary>
    public bool SyncEconomyCore()
    {
        string? missionPath = ResolveAppliedMapPath();
        if (missionPath is null)
        {
            return false;
        }

        bool updated = _typesService.SyncEconomyCore(_typesConfig, _typesConfig.CurrentMap, missionPath, LoadedSet());
        if (!updated)
        {
            _log.Warning("Failed to update cfgeconomycore.xml.");
        }

        return updated;
    }

    private HashSet<string> LoadedSet() => new(_loadedMods, StringComparer.OrdinalIgnoreCase);

    private string? ResolveAppliedMapPath()
    {
        if (string.IsNullOrEmpty(_typesConfig.CurrentMap))
        {
            _log.Warning("Apply a map first.");
            return null;
        }

        return ResolveMapPathForName(_typesConfig.CurrentMap);
    }

    private string? ResolveMapPathForName(string? mapName)
    {
        if (mapName is null)
        {
            _log.Warning("Select a map first.");
            return null;
        }

        string? path = _discoveredMaps
            .FirstOrDefault(m => string.Equals(m.Name, mapName, StringComparison.Ordinal))
            ?.Path;

        if (path is null)
        {
            _log.Error($"Map not found: {mapName}");
        }

        return path;
    }

    private async void ApplyMap()
    {
        string? missionPath = ResolveMapPathForName(SelectedMap);
        if (missionPath is null || SelectedMap is null)
        {
            return;
        }

        if (!await EnsureAppliedBeforeAsync("map switch"))
        {
            return;
        }

        ApplyMapCore(SelectedMap);
    }

    private void ApplyMapCore(string mapName)
    {
        _typesConfig.CurrentMap = mapName;
        _typesConfigStore.Save(_dataDirectory, _typesConfig);

        string mapId = GetMapId(mapName);

        if (!_serverConfig.UpdateTemplate(_serverPath, mapName))
        {
            _log.Warning("Failed to update the server template (serverDZ.cfg).");
        }

        if (!_batchFile.WriteServerProfile(Path.Combine(_serverPath, _batFileName), $"map_profiles\\{mapId}"))
        {
            _log.Warning("Failed to update the batch file serverProfile.");
        }

        try
        {
            _fileSystem.CreateDirectory(Path.Combine(_serverPath, "map_profiles", mapId));
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to create map_profiles directory: {ex.Message}");
        }

        OnPropertyChanged(nameof(CanApplyMap));
        NotifyCommandStates();
        RebuildRows();
        SyncEconomyCore();
        _log.Success($"Map switched to: {mapName}");
    }

    private async void ConfigureMod()
    {
        string? modName = SelectedMod;
        if (modName is null)
        {
            _log.Warning("Select a mod to configure first.");
            return;
        }

        string? missionPath = ResolveAppliedMapPath();
        if (missionPath is null)
        {
            return;
        }

        IReadOnlyList<string> files = _typesService.DiscoverTypeFiles(_workshopPath, modName);
        _log.Info($"Found {files.Count} candidate type file(s) in {modName}.");

        IReadOnlyList<string>? selected = _dialogs.PickTypeFiles(modName, files);
        if (selected is null || selected.Count == 0)
        {
            _log.Info("Configuration cancelled.");
            return;
        }

        if (!await EnsureAppliedBeforeAsync("types configuration"))
        {
            return;
        }

        TypesOperationResult result = _typesService.ConfigureMod(
            _typesConfig, _typesConfig.CurrentMap, missionPath, _workshopPath, modName, selected, LoadedSet());

        LogOperation(result, "Types configuration completed.");
    }

    private async void RemoveSelected()
    {
        List<TypesRowViewModel> rows = SelectedTypesRows.ToList();
        if (rows.Count == 0)
        {
            _log.Warning("Select rows in the table first.");
            return;
        }

        string? missionPath = ResolveAppliedMapPath();
        if (missionPath is null)
        {
            return;
        }

        if (!await EnsureAppliedBeforeAsync("removal"))
        {
            return;
        }

        bool success = true;
        var messages = new List<string>();
        foreach (IGrouping<string, TypesRowViewModel> group in rows.GroupBy(r => r.ModName, StringComparer.Ordinal))
        {
            var leaves = new HashSet<string>(group.Select(r => r.FileName), StringComparer.Ordinal);
            TypesOperationResult result = _typesService.RemoveFiles(
                _typesConfig, _typesConfig.CurrentMap, missionPath, group.Key, leaves, LoadedSet());
            messages.AddRange(result.Messages);
            if (!result.Success)
            {
                success = false;
            }
        }

        foreach (string message in messages)
        {
            _log.Info(message);
        }

        if (success)
        {
            _typesConfigStore.Save(_dataDirectory, _typesConfig);
            _log.Success("Removed selected types files.");
            RebuildRows();
        }
        else
        {
            _log.Error("Operation failed.");
        }
    }

    private async void CleanInvalid()
    {
        string? missionPath = ResolveAppliedMapPath();
        if (missionPath is null)
        {
            return;
        }

        if (!await EnsureAppliedBeforeAsync("cleanup"))
        {
            return;
        }

        var workshop = new HashSet<string>(_allMods, StringComparer.Ordinal);
        var active = new HashSet<string>(_loadedMods.Where(workshop.Contains), StringComparer.Ordinal);
        TypesOperationResult result = _typesService.CleanInvalid(
            _typesConfig, _typesConfig.CurrentMap, missionPath, active, LoadedSet());

        foreach (string message in result.Messages)
        {
            _log.Info(message);
        }

        if (result.Success)
        {
            _typesConfigStore.Save(_dataDirectory, _typesConfig);
            RebuildRows();
        }
        else
        {
            _log.Error("Operation failed.");
        }
    }

    private void LogOperation(TypesOperationResult result, string successMessage)
    {
        foreach (string message in result.Messages)
        {
            _log.Info(message);
        }

        if (result.Success)
        {
            _typesConfigStore.Save(_dataDirectory, _typesConfig);
            _log.Success(successMessage);
            RebuildRows();
        }
        else
        {
            _log.Error("Operation failed.");
        }
    }

    private void RebuildRows()
    {
        TypesRows.Clear();
        if (string.IsNullOrEmpty(_typesConfig.CurrentMap) ||
            !_typesConfig.Maps.TryGetValue(_typesConfig.CurrentMap, out MapTypesConfig? map))
        {
            return;
        }

        var valid = new HashSet<string>(_allMods, StringComparer.Ordinal);
        var loaded = LoadedSet();
        foreach (ModTypesEntry entry in map.Mods)
        {
            bool inactive = !valid.Contains(entry.ModName) || !loaded.Contains(entry.ModName);
            foreach (string generated in entry.GeneratedFiles)
            {
                TypesRows.Add(new TypesRowViewModel(entry.ModName, Path.GetFileName(generated), inactive));
            }
        }
    }

    private void NotifyCommandStates()
    {
        RemoveSelectedCommand.RaiseCanExecuteChanged();
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

    private static string GetMapId(string mapName)
    {
        int dot = mapName.LastIndexOf('.');
        return dot >= 0 ? mapName[(dot + 1)..] : mapName;
    }
}
