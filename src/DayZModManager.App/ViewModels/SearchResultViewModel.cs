namespace DayZModManager.App.ViewModels;

/// <summary>A single row in the unified mod search results.</summary>
public sealed class SearchResultViewModel : ViewModelBase
{
    private bool _isLoaded;
    private bool _isMissing;

    public SearchResultViewModel(string name, bool isLoaded, bool isMissing)
    {
        Name = name;
        _isLoaded = isLoaded;
        _isMissing = isMissing;
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
}
