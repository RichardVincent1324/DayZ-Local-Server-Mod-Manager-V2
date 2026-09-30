namespace DayZModManager.App.ViewModels;

/// <summary>A row in the types configuration grid.</summary>
public sealed class TypesRowViewModel : ViewModelBase
{
    private string _modName;
    private string _fileName;
    private string _fileType;
    private bool _isInactive;
    private bool _isUntracked;

    public TypesRowViewModel(string modName, string fileName, bool isInactive, bool isUntracked = false, string fileType = "")
    {
        _modName = modName;
        _fileName = fileName;
        _isInactive = isInactive;
        _isUntracked = isUntracked;
        _fileType = fileType;
    }

    public string ModName
    {
        get => _modName;
        set => SetField(ref _modName, value);
    }

    public string FileName
    {
        get => _fileName;
        set => SetField(ref _fileName, value);
    }

    /// <summary>The configured economy role: "type" or "spawnable"; empty for untracked files.</summary>
    public string FileType
    {
        get => _fileType;
        set => SetField(ref _fileType, value);
    }

    public bool IsInactive
    {
        get => _isInactive;
        set => SetField(ref _isInactive, value);
    }

    /// <summary>
    /// True when the file physically exists in db/ModTypes but is not tracked by
    /// this manager's types configuration.
    /// </summary>
    public bool IsUntracked
    {
        get => _isUntracked;
        set => SetField(ref _isUntracked, value);
    }
}
