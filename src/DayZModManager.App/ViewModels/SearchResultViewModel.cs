namespace DayZModManager.App.ViewModels;

/// <summary>A single row in the unified mod search results.</summary>
public sealed class SearchResultViewModel : ViewModelBase
{
    private bool _isLoaded;
    private bool _isMissing;
    private int? _position;

    public SearchResultViewModel(string name, bool isLoaded, bool isMissing, int? position = null)
    {
        Name = name;
        _isLoaded = isLoaded;
        _isMissing = isMissing;
        _position = position;
    }

    public string Name { get; }

    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetField(ref _isLoaded, value);
    }

    public bool IsMissing
    {
        get => _isMissing;
        set => SetField(ref _isMissing, value);
    }

    /// <summary>
    /// 1-based load position, or null when the mod is not loaded. Null keeps the
    /// row's index column empty instead of showing "#0".
    /// </summary>
    public int? Position
    {
        get => _position;
        set => SetField(ref _position, value);
    }
}
