using DayZModManager.Core;
using DayZModManager.Core.Models;
using DayZModManager.App.Services;

namespace DayZModManager.App.ViewModels;

/// <summary>Backs the "Settings" page: editable paths via Browse buttons.</summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;

    private string _workshopPath = string.Empty;
    private string _serverPath = string.Empty;
    private string _batFileName = string.Empty;
    private string _dataDirectory = string.Empty;
    private string _savedWorkshopPath = string.Empty;
    private string _savedServerPath = string.Empty;
    private string _savedBatFileName = string.Empty;
    private string _savedDataDirectory = string.Empty;
    private int _schemaVersion = 1;

    public SettingsViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;

        BrowseWorkshopCommand = new RelayCommand(BrowseWorkshop);
        BrowseServerCommand = new RelayCommand(BrowseServer);
        BrowseBatchFileCommand = new RelayCommand(BrowseBatchFile);
        BrowseDataDirectoryCommand = new RelayCommand(BrowseDataDirectory);
    }

    public string WorkshopPath
    {
        get => _workshopPath;
        set
        {
            if (SetField(ref _workshopPath, value.Trim()))
            {
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    public string ServerPath
    {
        get => _serverPath;
        set
        {
            if (SetField(ref _serverPath, value.Trim()))
            {
                OnPropertyChanged(nameof(EffectiveDataDirectory));
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    public string BatFileName
    {
        get => _batFileName;
        set
        {
            if (SetField(ref _batFileName, value.Trim()))
            {
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    /// <summary>
    /// Optional override for the data directory. Empty means the location is
    /// derived from <see cref="ServerPath"/> (or the legacy AppData directory).
    /// </summary>
    public string DataDirectory
    {
        get => _dataDirectory;
        set
        {
            if (SetField(ref _dataDirectory, value.Trim()))
            {
                OnPropertyChanged(nameof(EffectiveDataDirectory));
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    /// <summary>The effective data directory for the current override/server path.</summary>
    public string EffectiveDataDirectory => AppPaths.Resolve(DataDirectory, ServerPath);

    public bool IsDirty =>
        WorkshopPath != _savedWorkshopPath
        || ServerPath != _savedServerPath
        || BatFileName != _savedBatFileName
        || DataDirectory != _savedDataDirectory;

    public RelayCommand BrowseWorkshopCommand { get; }
    public RelayCommand BrowseServerCommand { get; }
    public RelayCommand BrowseBatchFileCommand { get; }
    public RelayCommand BrowseDataDirectoryCommand { get; }

    /// <summary>Raised after a browsed path has been set and should be applied immediately.</summary>
    public event Action? ApplyRequested;

    /// <summary>Raised after the user browses a new data directory; relocation happens immediately.</summary>
    public event Action? DataDirectoryChanged;

    /// <summary>Loads settings into the editable fields.</summary>
    public void Load(Settings settings)
    {
        _workshopPath = settings.WorkshopPath;
        _serverPath = settings.ServerPath;
        _batFileName = settings.BatFileName;
        _dataDirectory = settings.DataDirectory;
        _savedWorkshopPath = settings.WorkshopPath;
        _savedServerPath = settings.ServerPath;
        _savedBatFileName = settings.BatFileName;
        _savedDataDirectory = settings.DataDirectory;
        _schemaVersion = settings.SchemaVersion;

        OnPropertyChanged(nameof(WorkshopPath));
        OnPropertyChanged(nameof(ServerPath));
        OnPropertyChanged(nameof(BatFileName));
        OnPropertyChanged(nameof(DataDirectory));
        OnPropertyChanged(nameof(EffectiveDataDirectory));
        OnPropertyChanged(nameof(IsDirty));
    }

    public Settings ToSettings() =>
        new()
        {
            WorkshopPath = WorkshopPath,
            ServerPath = ServerPath,
            BatFileName = BatFileName,
            DataDirectory = DataDirectory,
            SchemaVersion = _schemaVersion,
        };

    public void MarkApplied(Settings settings)
    {
        _savedWorkshopPath = settings.WorkshopPath;
        _savedServerPath = settings.ServerPath;
        _savedBatFileName = settings.BatFileName;
        _savedDataDirectory = settings.DataDirectory;
        _schemaVersion = settings.SchemaVersion;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Marks the data directory as persisted after an immediate relocation.</summary>
    public void NotifyDataDirectoryApplied()
    {
        _savedDataDirectory = DataDirectory;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>True when both essential paths are set, so a Browse can auto-apply safely.</summary>
    private bool CanAutoApply =>
        !string.IsNullOrWhiteSpace(WorkshopPath) && !string.IsNullOrWhiteSpace(ServerPath);

    private void BrowseWorkshop()
    {
        string? folder = _dialogs.PickFolder("Select the Steam Workshop folder (!Workshop)");
        if (folder is not null)
        {
            WorkshopPath = folder;
            if (CanAutoApply)
            {
                ApplyRequested?.Invoke();
            }
        }
    }

    private void BrowseServer()
    {
        string? folder = _dialogs.PickFolder("Select the DayZ Server folder");
        if (folder is not null)
        {
            ServerPath = folder;
            if (CanAutoApply)
            {
                ApplyRequested?.Invoke();
            }
        }
    }

    private void BrowseBatchFile()
    {
        string? file = _dialogs.PickFile(
            "Select the launch batch file",
            "Batch files (*.bat)|*.bat|All files (*.*)|*.*",
            string.IsNullOrEmpty(ServerPath) ? AppContext.BaseDirectory : ServerPath);
        if (file is not null)
        {
            BatFileName = file;
            if (CanAutoApply)
            {
                ApplyRequested?.Invoke();
            }
        }
    }

    private void BrowseDataDirectory()
    {
        string? folder = _dialogs.PickFolder("Select the data directory");
        if (folder is not null)
        {
            DataDirectory = folder;
            DataDirectoryChanged?.Invoke();
        }
    }
}
