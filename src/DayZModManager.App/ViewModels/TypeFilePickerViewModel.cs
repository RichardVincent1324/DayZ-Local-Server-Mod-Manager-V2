using System.Collections.ObjectModel;
using System.IO;
using DayZModManager.Core.Services;

namespace DayZModManager.App.ViewModels;

/// <summary>A choice in the unrecognized-file role dropdown.</summary>
public sealed record TypeFileRoleChoice(TypesFileRole Role, string Display);

/// <summary>A selectable XML file in the types picker.</summary>
public sealed class TypeFileOptionViewModel : ViewModelBase
{
    private bool _isChecked;
    private TypesFileRole _role;

    public TypeFileOptionViewModel(string fullPath, string displayPath, bool isRecognized)
    {
        FullPath = fullPath;
        DisplayPath = displayPath;
        IsRecognized = isRecognized;
        _role = isRecognized
            ? (TypesFileRoles.IsSpawnable(Path.GetFileName(fullPath)) ? TypesFileRole.SpawnableTypes : TypesFileRole.Types)
            : TypesFileRole.Types;
    }

    public string FullPath { get; }

    public string DisplayPath { get; }

    /// <summary>True when the file name carries a "type"/"spawnable" keyword and its role cannot be changed.</summary>
    public bool IsRecognized { get; }

    public bool IsUnrecognized => !IsRecognized;

    /// <summary>The roles offered for an unrecognized file.</summary>
    public IReadOnlyList<TypeFileRoleChoice> RoleChoices => TypeFilePickerViewModel.RoleChoices;

    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }

    /// <summary>
    /// The role the file will be copied under. Only meaningful for unrecognized
    /// files; recognized files keep their filename-derived role.
    /// </summary>
    public TypesFileRole Role
    {
        get => _role;
        set => SetField(ref _role, value);
    }
}

/// <summary>
/// Backs the types-file picker dialog. All discovered XML files are listed;
/// recognized files (name contains "type"/"spawnable") come first, with
/// unrecognized files separated at the bottom where the user may assign them a
/// role. Only the files already configured for the mod are pre-selected, so a
/// re-run with no edits does not silently change anything.
/// </summary>
public sealed class TypeFilePickerViewModel : ViewModelBase
{
    public TypeFilePickerViewModel(
        string modName,
        IReadOnlyList<string> files,
        string basePath,
        IReadOnlySet<string>? activeFiles = null,
        IReadOnlyDictionary<string, TypesFileRole>? activeRoles = null)
    {
        ModName = modName;
        foreach (string file in files)
        {
            string display = file.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
                ? file[basePath.Length..].TrimStart('\\', '/')
                : file;
            bool recognized = TypesFileRoles.IsRecognized(Path.GetFileName(file));
            var option = new TypeFileOptionViewModel(file, display, recognized);

            if (activeFiles?.Contains(file) == true)
            {
                option.IsChecked = true;
                if (!recognized && activeRoles is not null && activeRoles.TryGetValue(file, out TypesFileRole role))
                {
                    option.Role = role;
                }
            }

            if (recognized)
            {
                RecognizedOptions.Add(option);
            }
            else
            {
                UnrecognizedOptions.Add(option);
            }
        }
    }

    public static IReadOnlyList<TypeFileRoleChoice> RoleChoices { get; } = new[]
    {
        new TypeFileRoleChoice(TypesFileRole.Types, "Types"),
        new TypeFileRoleChoice(TypesFileRole.SpawnableTypes, "Spawnable"),
    };

    public string ModName { get; }

    /// <summary>Files whose name already classifies them as types/spawnabletypes.</summary>
    public ObservableCollection<TypeFileOptionViewModel> RecognizedOptions { get; } = new();

    /// <summary>Files with no economy keyword; the user chooses their role.</summary>
    public ObservableCollection<TypeFileOptionViewModel> UnrecognizedOptions { get; } = new();

    public bool HasUnrecognizedOptions => UnrecognizedOptions.Count > 0;

    public IReadOnlyList<TypeFileSelection> GetSelections() =>
        RecognizedOptions
            .Concat(UnrecognizedOptions)
            .Where(option => option.IsChecked)
            .Select(option => new TypeFileSelection(option.FullPath, option.Role))
            .ToList();
}
