using System.Collections.ObjectModel;
using System.ComponentModel;
using DayZModManager.Core.Services;

namespace DayZModManager.App.ViewModels;

/// <summary>A selectable types file in the picker.</summary>
public sealed class TypeFileOptionViewModel : ViewModelBase
{
    private bool _isChecked;

    public TypeFileOptionViewModel(string fullPath, string displayPath, string role)
    {
        FullPath = fullPath;
        DisplayPath = displayPath;
        Role = role;
    }

    public string FullPath { get; }

    public string DisplayPath { get; }

    /// <summary>The DayZ role of the file: "types" or "spawnabletypes".</summary>
    public string Role { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }
}

/// <summary>
/// Backs the types-file picker dialog. Only the files already configured for the
/// mod are pre-selected, and a mod may have at most one active file per role
/// ("types" / "spawnabletypes"): checking a file unchecks the other candidates of
/// the same role so mods shipping alternative sets (e.g. Casual vs Hardcore)
/// cannot end up with two active "types" files.
/// </summary>
public sealed class TypeFilePickerViewModel : ViewModelBase
{
    public TypeFilePickerViewModel(
        string modName,
        IReadOnlyList<string> files,
        string basePath,
        IReadOnlySet<string>? activeFiles = null)
    {
        ModName = modName;
        foreach (string file in files)
        {
            string display = file.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
                ? file[basePath.Length..].TrimStart('\\', '/')
                : file;
            string role = TypesFileRoles.RoleOf(System.IO.Path.GetFileName(file));
            bool isActive = activeFiles?.Contains(file) == true;
            Options.Add(new TypeFileOptionViewModel(file, display, role) { IsChecked = isActive });
        }

        foreach (TypeFileOptionViewModel option in Options)
        {
            option.PropertyChanged += OnOptionChanged;
        }

        EnforceOnePerRole();
    }

    public string ModName { get; }

    public ObservableCollection<TypeFileOptionViewModel> Options { get; } = new();

    public IReadOnlyList<string> GetSelectedFiles() =>
        Options.Where(o => o.IsChecked).Select(o => o.FullPath).ToList();

    /// <summary>
    /// Keeps at most one checked option per role, preserving the earliest. This
    /// reconciles configurations created before the one-per-role rule existed.
    /// </summary>
    private void EnforceOnePerRole()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TypeFileOptionViewModel option in Options)
        {
            if (!option.IsChecked || seen.Add(option.Role))
            {
                continue;
            }

            option.IsChecked = false;
        }
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TypeFileOptionViewModel.IsChecked))
        {
            return;
        }

        var changed = (TypeFileOptionViewModel)sender!;
        if (!changed.IsChecked)
        {
            return;
        }

        foreach (TypeFileOptionViewModel other in Options)
        {
            if (other != changed && other.IsChecked && string.Equals(other.Role, changed.Role, StringComparison.Ordinal))
            {
                other.IsChecked = false;
            }
        }
    }
}
