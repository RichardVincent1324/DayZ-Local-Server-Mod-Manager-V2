using System.Collections.ObjectModel;
using DayZModManager.App.ViewModels;

namespace DayZModManager.App.ViewModels;

/// <summary>A selectable types file in the picker.</summary>
public sealed class TypeFileOptionViewModel : ViewModelBase
{
    private bool _isChecked;

    public TypeFileOptionViewModel(string fullPath, string displayPath)
    {
        FullPath = fullPath;
        DisplayPath = displayPath;
    }

    public string FullPath { get; }

    public string DisplayPath { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }
}

/// <summary>Backs the types-file picker dialog.</summary>
public sealed class TypeFilePickerViewModel : ViewModelBase
{
    public TypeFilePickerViewModel(string modName, IReadOnlyList<string> files, string basePath)
    {
        ModName = modName;
        foreach (string file in files)
        {
            string display = file.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
                ? file[basePath.Length..].TrimStart('\\', '/')
                : file;
            Options.Add(new TypeFileOptionViewModel(file, display) { IsChecked = true });
        }
    }

    public string ModName { get; }

    public ObservableCollection<TypeFileOptionViewModel> Options { get; } = new();

    public IReadOnlyList<string> GetSelectedFiles() =>
        Options.Where(o => o.IsChecked).Select(o => o.FullPath).ToList();
}
