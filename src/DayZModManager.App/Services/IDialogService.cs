using DayZModManager.Core.Services;

namespace DayZModManager.App.Services;

/// <summary>Input gathered by the Add Preset dialog.</summary>
/// <param name="Name">The new preset's name.</param>
/// <param name="CopyProfilesFromDefault">True to seed the preset from the default preset's profiles.</param>
public sealed record AddPresetRequest(string Name, bool CopyProfilesFromDefault);

/// <summary>Input gathered by the Duplicate Preset dialog.</summary>
/// <param name="Name">The new (duplicated) preset's name.</param>
/// <param name="CopyProfiles">True to also copy the current preset's profiles data.</param>
public sealed record DuplicatePresetRequest(string Name, bool CopyProfiles);

/// <summary>Abstraction over modal UI interactions (message boxes, pickers).</summary>
public interface IDialogService
{
    void ShowMessage(string message, string title, bool isError = false);

    bool Confirm(string message, string title);

    /// <summary>
    /// Confirmation whose <paramref name="warning"/> (when non-empty) is rendered
    /// prominently in red above the Yes/No buttons so it cannot be missed. A plain
    /// <paramref name="note"/> (e.g. a path for investigation) is shown in normal
    /// text below the warning.
    /// </summary>
    bool ConfirmWithWarning(string message, string title, string warning, string note = "");

    /// <summary>
    /// Shows an informational window with the same prominent red warning banner
    /// as <see cref="ConfirmWithWarning"/>, but with a single OK button. Used to
    /// report a blocked operation.
    /// </summary>
    void ShowWarning(string message, string title, string warning, string note = "");

    /// <summary>Opens a folder picker. Returns the chosen path, or null if cancelled.</summary>
    string? PickFolder(string title = "Select a folder");

    /// <summary>Opens a file picker. Returns the chosen file path, or null if cancelled.</summary>
    string? PickFile(string title, string filter, string initialDirectory);

    /// <summary>
    /// Opens the types-file picker for a mod. <paramref name="activeFiles"/> is the
    /// subset of <paramref name="files"/> that are currently configured for the
    /// mod; those are pre-selected. <paramref name="activeRoles"/> carries the
    /// role of any pre-selected unrecognized file. <paramref name="modFolderPath"/>
    /// is the mod's Workshop folder, offered as a shortcut to inspect unrecognized
    /// files. Returns the selected files and their roles, or null if the user
    /// cancelled.
    /// </summary>
    IReadOnlyList<TypeFileSelection>? PickTypeFiles(
        string modName,
        IReadOnlyList<string> files,
        IReadOnlySet<string>? activeFiles = null,
        IReadOnlyDictionary<string, TypesFileRole>? activeRoles = null,
        string? modFolderPath = null);

    /// <summary>
    /// Prompts for a single line of text. Returns the entered text, or null when
    /// the user cancelled.
    /// </summary>
    string? AskText(string title, string prompt, string defaultValue = "");

    /// <summary>
    /// Opens the Add Preset dialog for a map. Returns the requested name and
    /// whether to copy the default preset's profiles, or null when cancelled.
    /// </summary>
    AddPresetRequest? AskAddPreset(string mapName);

    /// <summary>
    /// Opens the Duplicate Preset dialog for an existing preset. Returns the
    /// requested name and whether to copy the current preset's profiles, or null
    /// when cancelled.
    /// </summary>
    DuplicatePresetRequest? AskDuplicatePreset(string sourcePresetName);
}
