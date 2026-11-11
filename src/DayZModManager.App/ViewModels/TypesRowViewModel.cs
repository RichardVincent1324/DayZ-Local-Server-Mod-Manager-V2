namespace DayZModManager.App.ViewModels;

/// <summary>A row in the types configuration grid.</summary>
public sealed class TypesRowViewModel : ViewModelBase
{
    private readonly Func<TypesRowViewModel, string, string, bool> _renameRequested;
    private readonly Func<TypesRowViewModel, string?, string?, bool> _fileTypeChanged;

    private string _fileName;
    private string? _fileType;

    public TypesRowViewModel(
        string fileName,
        string? fileType,
        Func<TypesRowViewModel, string, string, bool> renameRequested,
        Func<TypesRowViewModel, string?, string?, bool> fileTypeChanged)
    {
        _fileName = fileName;
        _fileType = fileType;
        _renameRequested = renameRequested;
        _fileTypeChanged = fileTypeChanged;
    }

    /// <summary>
    /// The physical file name. The setter runs only when the DataGrid commits an
    /// edit (LostFocus or Enter); Escape cancels the edit before this is called.
    /// </summary>
    public string FileName
    {
        get => _fileName;
        set
        {
            string newName = value ?? string.Empty;
            if (string.Equals(_fileName, newName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!_renameRequested(this, _fileName, newName))
            {
                // Restore the displayed value; no file was renamed.
                OnPropertyChanged();
                return;
            }

            SetField(ref _fileName, newName);
        }
    }

    /// <summary>The assigned economy role ("types" / "spawnabletypes"), or null when unassigned.</summary>
    public string? FileType
    {
        get => _fileType;
        private set => SetField(ref _fileType, value);
    }

    /// <summary>
    /// UI-facing value for the File Type ComboBox. The placeholder replaces null so
    /// WPF never has to render a raw null item.
    /// </summary>
    public string FileTypeSelection
    {
        get => _fileType ?? PresetTypesViewModel.UnassignedFileTypeLabel;
        set
        {
            string? selected = string.Equals(value, PresetTypesViewModel.UnassignedFileTypeLabel, StringComparison.Ordinal)
                ? null
                : value;

            if (string.Equals(_fileType, selected, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string? previous = _fileType;
            if (!_fileTypeChanged(this, previous, selected))
            {
                OnPropertyChanged(nameof(FileTypeSelection));
                OnPropertyChanged(nameof(FileType));
                return;
            }

            FileType = selected;
            OnPropertyChanged(nameof(FileTypeSelection));
        }
    }
}
