using DayZModManager.Core;

namespace DayZModManager.App.ViewModels;

/// <summary>
/// A preset row in the Map &amp; Types "Presets" list. Presets are the unit of
/// server environment; selecting one makes it the active preset.
/// </summary>
public sealed class PresetItemViewModel : ViewModelBase
{
    public PresetItemViewModel(string name)
    {
        Name = name;
        IsDefault = PresetPaths.IsDefaultPreset(name);
    }

    /// <summary>The preset's folder name (its stable key).</summary>
    public string Name { get; }

    /// <summary>True for the reserved, auto-created default preset.</summary>
    public bool IsDefault { get; }

    /// <summary>User-facing label; the reserved default is shown with a hint.</summary>
    public string DisplayName => IsDefault ? $"{Name} (default)" : Name;
}
