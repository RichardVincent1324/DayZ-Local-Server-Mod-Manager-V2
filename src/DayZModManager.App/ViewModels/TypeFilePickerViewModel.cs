using System.Collections.ObjectModel;
using System.IO;

namespace DayZModManager.App.ViewModels;

/// <summary>A selectable XML file in the type-file import picker.</summary>
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

/// <summary>
/// Backs the type-file import dialog: every XML file discovered in a mod is
/// listed with a checkbox. The user selects the files to copy into the active
/// preset's type_files folder; no roles are assigned here.
/// </summary>
public sealed class TypeFilePickerViewModel : ViewModelBase
{
    public TypeFilePickerViewModel(
        string modName,
        IReadOnlyList<string> files,
        string basePath)
    {
        ModName = modName;
        foreach (string file in files)
        {
            string display = file.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
                ? file[basePath.Length..].TrimStart('\\', '/')
                : file;
            var option = new TypeFileOptionViewModel(file, display);
            Files.Add(option);

            if (IsRecognized(file))
            {
                RecognizedOptions.Add(option);
            }
            else
            {
                UnrecognizedOptions.Add(option);
            }
        }
    }

    public string ModName { get; }

    public ObservableCollection<TypeFileOptionViewModel> Files { get; } = new();

    /// <summary>Files whose names contain "type" or "spawnable"; shown first.</summary>
    public ObservableCollection<TypeFileOptionViewModel> RecognizedOptions { get; } = new();

    /// <summary>All remaining files; shown after the separator.</summary>
    public ObservableCollection<TypeFileOptionViewModel> UnrecognizedOptions { get; } = new();

    /// <summary>True only when there is something on both sides of the separator.</summary>
    public bool ShowGroupSeparator =>
        RecognizedOptions.Count > 0 && UnrecognizedOptions.Count > 0;

    public IReadOnlyList<string> GetSelectedFiles() =>
        Files
            .Where(option => option.IsChecked)
            .Select(option => option.FullPath)
            .ToList();

    private static bool IsRecognized(string file)
    {
        string fileName = Path.GetFileName(file);
        return fileName.Contains("type", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("spawnable", StringComparison.OrdinalIgnoreCase);
    }
}
