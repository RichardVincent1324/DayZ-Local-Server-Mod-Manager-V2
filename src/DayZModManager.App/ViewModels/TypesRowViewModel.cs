namespace DayZModManager.App.ViewModels;

/// <summary>A row in the types configuration grid.</summary>
public sealed class TypesRowViewModel : ViewModelBase
{
    private string _modName;
    private string _fileName;
    private bool _isInactive;

    public TypesRowViewModel(string modName, string fileName, bool isInactive)
    {
        _modName = modName;
        _fileName = fileName;
        _isInactive = isInactive;
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

    public bool IsInactive
    {
        get => _isInactive;
        set => SetField(ref _isInactive, value);
    }
}
