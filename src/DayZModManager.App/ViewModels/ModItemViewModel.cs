namespace DayZModManager.App.ViewModels;

/// <summary>A mod shown in one of the two lists. Missing mods are greyed out in the UI.</summary>
public sealed class ModItemViewModel : ViewModelBase
{
    private string _name;
    private bool _isMissing;
    private int _position;

    public ModItemViewModel(string name, bool isMissing)
    {
        _name = name;
        _isMissing = isMissing;
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public bool IsMissing
    {
        get => _isMissing;
        set => SetField(ref _isMissing, value);
    }

    /// <summary>1-based position in the loaded list, i.e. the mod's load order.</summary>
    public int Position
    {
        get => _position;
        set => SetField(ref _position, value);
    }
}
